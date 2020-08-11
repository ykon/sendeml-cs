/**
 * Copyright (c) Yuki Ono.
 * Licensed under the MIT License.
 */

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SendEML {
    using SendCmd = Func<string, string>;
    public class Program {
        public const double VERSION = 1.4;

        public const byte CR = (byte)'\r';
        public const byte LF = (byte)'\n';
        public const string CRLF = "\r\n";

        static readonly byte[] DATE_BYTES = Encoding.UTF8.GetBytes("Date:");
        static readonly byte[] MESSAGE_ID_BYTES = Encoding.UTF8.GetBytes("Message-ID:");

        public static bool MatchHeaderField(byte[] line, byte[] header) {
            if (line.Length < header.Length)
                return false;

            for (var i = 0; i < header.Length; i++) {
                if (header[i] != line[i])
                    return false;
            }

            return true;
        }

        public static bool IsDateLine(byte[] line) {
            return MatchHeaderField(line, DATE_BYTES);
        }

        public static string MakeNowDateLine() {
            var us = new CultureInfo("en-US");
            var now = DateTimeOffset.Now;
            var offset = now.ToString("zzz", us).Replace(":", "");
            return "Date: " + now.ToString("ddd, dd MMM yyyy HH:mm:ss ", us) + offset + CRLF;
        }

        public static bool IsMessageIdLine(byte[] line) {
            return MatchHeaderField(line, MESSAGE_ID_BYTES);
        }

        static readonly Random random = new Random();
        public static string MakeRandomMessageIdLine() {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            const int length = 62;
            var randStr = new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
            return $"Message-ID: <{randStr}>" + CRLF;
        }

        public static int FindCrIndex(byte[] buf, int offset) {
            return Array.IndexOf(buf, CR, offset);
        }

        public static int FindLfIndex(byte[] buf, int offset) {
            return Array.IndexOf(buf, LF, offset);
        }

        public static ImmutableList<int> FindAllLfIndices(byte[] buf) {
            var indices = ImmutableList.CreateBuilder<int>();
            var offset = 0;
            while (true) {
                var idx = FindLfIndex(buf, offset);
                if (idx == -1)
                    return indices.ToImmutable();

                indices.Add(idx);
                offset = idx + 1;
            }
        }

        public static byte[] CopyNew(byte[] srcBuf, int offset, int count) {
            var destBuf = new byte[count];
            Buffer.BlockCopy(srcBuf, offset, destBuf, 0, destBuf.Length);
            return destBuf;
        }

        public static ImmutableList<byte[]> GetRawLines(byte[] fileBuf) {
            var offset = 0;
            return FindAllLfIndices(fileBuf).Add(fileBuf.Length - 1).Select(i => {
                var line = CopyNew(fileBuf, offset, i - offset + 1);
                offset = i + 1;
                return line;
            }).ToImmutableList();
        }

        public static bool IsNotUpdate(bool updateDate, bool updateMessageId) {
            return !updateDate && !updateMessageId;
        }

        public static byte[] ReplaceHeader(byte[] header, bool updateDate, bool updateMessageId) {
            if (IsNotUpdate(updateDate, updateMessageId))
                return header;

            static void ReplaceLine(ImmutableList<byte[]>.Builder lines, bool update, Predicate<byte[]> matchLine, Func<string> makeLine) {
                if (update) {
                    var idx = lines.FindIndex(matchLine);
                    if (idx != -1)
                        lines[idx] = Encoding.UTF8.GetBytes(makeLine());
                }
            }

            var replLines = GetRawLines(header).ToBuilder();
            ReplaceLine(replLines, updateDate, IsDateLine, MakeNowDateLine);
            ReplaceLine(replLines, updateMessageId, IsMessageIdLine, MakeRandomMessageIdLine);
            return ConcatRawLines(replLines.ToImmutable());
        }

        public static byte[] ConcatRawLines(ImmutableList<byte[]> lines) {
            var buf = new byte[lines.Sum(l => l.Length)];
            var offset = 0;
            foreach (var l in lines) {
                Buffer.BlockCopy(l, 0, buf, offset, l.Length);
                offset += l.Length;
            }
            return buf;
        }

        public static readonly byte[] EMPTY_LINE = new[] { CR, LF, CR, LF };

        public static byte[] CombineMail(byte[] header, byte[] body) {
            var mail = new byte[header.Length + EMPTY_LINE.Length + body.Length];
            Buffer.BlockCopy(header, 0, mail, 0, header.Length);
            Buffer.BlockCopy(EMPTY_LINE, 0, mail, header.Length, EMPTY_LINE.Length);
            Buffer.BlockCopy(body, 0, mail, header.Length + EMPTY_LINE.Length, body.Length);
            return mail;
        }

        public static int FindEmptyLine(byte[] fileBuf) {
            var offset = 0;
            while (true) {
                var idx = FindCrIndex(fileBuf, offset);
                if (idx == -1 || (idx + 3) >= fileBuf.Length)
                    return -1;

                if (fileBuf[idx + 1] == LF && fileBuf[idx + 2] == CR && fileBuf[idx + 3] == LF)
                    return idx;

                offset = idx + 1;
            }
        }

        public static (byte[], byte[])? SplitMail(byte[] fileBuf) {
            var idx = FindEmptyLine(fileBuf);
            if (idx == -1)
                return null;

            var header = CopyNew(fileBuf, 0, idx);
            var bodyIdx = idx + EMPTY_LINE.Length;
            var body = CopyNew(fileBuf, bodyIdx, fileBuf.Length - bodyIdx);
            return (header, body);
        }

        public static byte[] ReplaceRawBytes(byte[] fileBuf, bool updateDate, bool updateMessageId) {
            if (IsNotUpdate(updateDate, updateMessageId))
                return fileBuf;

            var mail = SplitMail(fileBuf);
            if (!mail.HasValue)
                throw new IOException("Invalid mail");

            var (header, body) = mail.Value;
            var replHeader = ReplaceHeader(header, updateDate, updateMessageId);
            return CombineMail(replHeader, body);
        }

        static volatile bool USE_PARALLEL = false;

        public static string GetCurrentIdPrefix() {
            return USE_PARALLEL ? $"id: {Task.CurrentId}, " : "";
        }

        public static void SendRawBytes(Stream stream, string file, bool updateDate, bool updateMessageId) {
            Console.WriteLine(GetCurrentIdPrefix() + $"send: {file}");

            var path = Path.GetFullPath(file);
            var buf = ReplaceRawBytes(File.ReadAllBytes(path), updateDate, updateMessageId);
            stream.Write(buf, 0, buf.Length);
            stream.Flush();
        }

#nullable enable
        public class Settings {
            public string? SmtpHost { get; set; }
            public int? SmtpPort { get; set; }
            public string? FromAddress { get; set; }
            public ImmutableList<string>? ToAddress { get; set; }
            public ImmutableList<string>? EmlFile { get; set; }
            public bool UpdateDate { get; set; } = true;
            public bool UpdateMessageId { get; set; } = true;
            public bool UseParallel { get; set; } = false;
        }
#nullable disable

        public static Settings GetSettingsFromText(string text) {
            var options = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            return JsonSerializer.Deserialize<Settings>(text, options);
        }

        public static Settings GetSettings(string file) {
            var path = Path.GetFullPath(file);
            return GetSettingsFromText(File.ReadAllText(path));
        }

        static readonly Regex LAST_REPLY_REGEX = new Regex(@"^\d{3} .+", RegexOptions.Compiled);

        public static bool IsLastReply(string line) {
            return LAST_REPLY_REGEX.IsMatch(line);
        }

        public static bool IsPositiveReply(string line) {
            return line.FirstOrDefault() switch {
                '2' => true,
                '3' => true,
                _ => false
            };
        }

        public static string RecvLine(StreamReader reader) {
            while (true) {
                var line = reader.ReadLine()?.Trim() ?? throw new IOException("Connection closed by foreign host");
                Console.WriteLine(GetCurrentIdPrefix() + $"recv: {line}");

                if (IsLastReply(line)) {
                    if (IsPositiveReply(line))
                        return line;

                    throw new IOException(line);
                }
            }
        }

        public static string ReplaceCrlfDot(string cmd) {
            return cmd == $"{CRLF}." ? "<CRLF>." : cmd;
        }

        public static void SendLine(Stream output, string cmd) {
            Console.WriteLine(GetCurrentIdPrefix() + "send: " + ReplaceCrlfDot(cmd));

            var buf = Encoding.UTF8.GetBytes(cmd + CRLF);
            output.Write(buf, 0, buf.Length);
            output.Flush();
        }

        public static SendCmd MakeSendCmd(StreamReader reader) {
            return cmd => {
                SendLine(reader.BaseStream, cmd);
                return RecvLine(reader);
            };
        }

        public static void SendHello(SendCmd send) {
            send("EHLO localhost");
        }

        public static void SendFrom(SendCmd send, string fromAddr) {
            send($"MAIL FROM: <{fromAddr}>");
        }

        public static void SendRcptTo(SendCmd send, ImmutableList<string> toAddrs) {
            foreach (var addr in toAddrs)
                send($"RCPT TO: <{addr}>");
        }

        public static void SendData(SendCmd send) {
            send("DATA");
        }

        public static void SendCrlfDot(SendCmd send) {
            send($"{CRLF}.");
        }

        public static void SendQuit(SendCmd send) {
            send("QUIT");
        }

        public static void SendRset(SendCmd send) {
            send("RSET");
        }

        public static void SendMessages(Settings settings, ImmutableList<string> emlFiles) {
            using var socket = new TcpClient(settings.SmtpHost, (int)settings.SmtpPort!);
            var stream = socket.GetStream();
            stream.ReadTimeout = 1000;

            var reader = new StreamReader(stream);
            var send = MakeSendCmd(reader);

            RecvLine(reader);
            SendHello(send);

            var mailSent = false;
            foreach (var file in emlFiles) {
                if (!File.Exists(file)) {
                    Console.WriteLine($"{file}: EML file does not exist");
                    continue;
                }

                if (mailSent) {
                    Console.WriteLine("---");
                    SendRset(send);
                }

                SendFrom(send, settings.FromAddress);
                SendRcptTo(send, settings.ToAddress);
                SendData(send);
                SendRawBytes(stream, file, settings.UpdateDate, settings.UpdateMessageId);
                SendCrlfDot(send);
                mailSent = true;
            }

            SendQuit(send);
        }

        public static void SendOneMessage(Settings settings, string file) {
            SendMessages(settings, ImmutableList.Create(file));
        }

        public static string MakeJsonSample() {
            return @"{
    ""smtpHost"": ""172.16.3.151"",
    ""smtpPort"": 25,
    ""fromAddress"": ""a001@ah62.example.jp"",
    ""toAddress"": [
        ""a001@ah62.example.jp"",
        ""a002@ah62.example.jp"",
        ""a003@ah62.example.jp""
    ],
    ""emlFile"": [
        ""test1.eml"",
        ""test2.eml"",
        ""test3.eml""
    ],
    ""updateDate"": true,
    ""updateMessageId"": true,
    ""useParallel"": false
}";
        }

        public static void WriteUsage() {
            Console.WriteLine("Usage: {self} json_file ...");
            Console.WriteLine("---");

            Console.WriteLine("json_file sample:");
            Console.WriteLine(MakeJsonSample());
        }

        public static void WriteVersion() {
            Console.WriteLine($"SendEML / Version: {VERSION}");
        }

        public static void CheckSettings(Settings settings) {
            static string GetNullKey(Settings s) {
                return s.SmtpHost == null ? "smtpHost"
                    : s.SmtpPort == null ? "smtpPort"
                    : s.FromAddress == null ? "fromAddress"
                    : s.ToAddress == null ? "toAddress"
                    : s.EmlFile == null ? "emlFile"
                    : "";
            }

            var key = GetNullKey(settings);
            if (key != "")
                throw new IOException($"{key} key does not exist");
        }

        public static void ProcJsonFile(string jsonFile) {
            if (!File.Exists(jsonFile))
                throw new IOException("Json file does not exist");

            var settings = GetSettings(jsonFile);
            CheckSettings(settings);

            if (settings.UseParallel) {
                USE_PARALLEL = true;
                settings.EmlFile.AsParallel().ForAll(f => SendOneMessage(settings, f));
            } else {
                SendMessages(settings, settings.EmlFile);
            }
        }

        static void Main(string[] args) {
            if (args.Length == 0) {
                WriteUsage();
                return;
            }

            if (args[0] == "--version") {
                WriteVersion();
                return;
            }

            foreach (var jsonFile in args) {
                try {
                    ProcJsonFile(jsonFile);
                } catch (Exception e) {
                    Console.WriteLine($"{jsonFile}: {e.Message}");
                }
            }
        }
    }
}
