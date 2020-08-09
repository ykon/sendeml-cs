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
        public const double VERSION = 1.3;

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
            var rand_str = new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
            return $"Message-ID: <{rand_str}>" + CRLF;
        }

        public static int FindLfIndex(byte[] file_buf, int offset) {
            return Array.IndexOf(file_buf, LF, offset);
        }

        public static ImmutableList<int> FindAllLfIndices(byte[] file_buf) {
            var indices = ImmutableList.CreateBuilder<int>();
            var offset = 0;
            while (true) {
                var idx = FindLfIndex(file_buf, offset);
                if (idx == -1)
                    return indices.ToImmutable();

                indices.Add(idx);
                offset = idx + 1;
            }
        }

        public static byte[] CopyNew(byte[] src_buf, int offset, int count) {
            var dest_buf = new byte[count];
            Buffer.BlockCopy(src_buf, offset, dest_buf, 0, dest_buf.Length);
            return dest_buf;
        }

        public static ImmutableList<byte[]> GetRawLines(byte[] file_buf) {
            var offset = 0;
            return FindAllLfIndices(file_buf).Add(file_buf.Length - 1).Select(i => {
                var line = CopyNew(file_buf, offset, i - offset + 1);
                offset = i + 1;
                return line;
            }).ToImmutableList();
        }

        public static bool IsNotUpdate(bool update_date, bool update_message_id) {
            return !update_date && !update_message_id;
        }

        public static byte[] ReplaceHeader(byte[] header, bool update_date, bool update_message_id) {
            if (IsNotUpdate(update_date, update_message_id))
                return header;

            static void ReplaceLine(ImmutableList<byte[]>.Builder lines, bool update, Predicate<byte[]> match_line, Func<string> make_line) {
                if (update) {
                    var idx = lines.FindIndex(match_line);
                    if (idx != -1)
                        lines[idx] = Encoding.UTF8.GetBytes(make_line());
                }
            }

            var repl_lines = GetRawLines(header).ToBuilder();
            ReplaceLine(repl_lines, update_date, IsDateLine, MakeNowDateLine);
            ReplaceLine(repl_lines, update_message_id, IsMessageIdLine, MakeRandomMessageIdLine);
            return ConcatRawLines(repl_lines.ToImmutable());
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

        public static int FindEmptyLine(byte[] file_buf) {
            var offset = 0;
            while (true) {
                var idx = Array.IndexOf(file_buf, CR, offset);
                if (idx == -1 || (idx + 3) >= file_buf.Length)
                    return -1;

                if (file_buf[idx + 1] == LF && file_buf[idx + 2] == CR && file_buf[idx + 3] == LF)
                    return idx;

                offset = idx + 1;
            }
        }

        public static (byte[], byte[])? SplitMail(byte[] file_buf) {
            var idx = FindEmptyLine(file_buf);
            if (idx == -1)
                return null;

            var header = CopyNew(file_buf, 0, idx);
            var body_idx = idx + EMPTY_LINE.Length;
            var body = CopyNew(file_buf, body_idx, file_buf.Length - body_idx);
            return (header, body);
        }

        public static byte[] ReplaceRawBytes(byte[] file_buf, bool update_date, bool update_message_id) {
            if (IsNotUpdate(update_date, update_message_id))
                return file_buf;

            var mail = SplitMail(file_buf);
            if (!mail.HasValue)
                throw new IOException("Invalid mail");

            var (header, body) = mail.Value;
            var repl_header = ReplaceHeader(header, update_date, update_message_id);
            return CombineMail(repl_header, body);
        }

        static volatile bool USE_PARALLEL = false;

        public static string GetCurrentIdPrefix() {
            return USE_PARALLEL ? $"id: {Task.CurrentId}, " : "";
        }

        public static void SendRawBytes(Stream stream, string file, bool update_date, bool update_message_id) {
            Console.WriteLine(GetCurrentIdPrefix() + $"send: {file}");

            var path = Path.GetFullPath(file);
            var buf = ReplaceRawBytes(File.ReadAllBytes(path), update_date, update_message_id);
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

        public static void SendFrom(SendCmd send, string from_addr) {
            send($"MAIL FROM: <{from_addr}>");
        }

        public static void SendRcptTo(SendCmd send, ImmutableList<string> to_addrs) {
            foreach (var addr in to_addrs)
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

        public static void SendMessages(Settings settings, ImmutableList<string> eml_files) {
            using var socket = new TcpClient(settings.SmtpHost, (int)settings.SmtpPort!);
            var stream = socket.GetStream();
            stream.ReadTimeout = 1000;

            var reader = new StreamReader(stream);
            var send = MakeSendCmd(reader);

            RecvLine(reader);
            SendHello(send);

            var mail_sent = false;
            foreach (var file in eml_files) {
                if (!File.Exists(file)) {
                    Console.WriteLine($"{file}: EML file does not exist");
                    continue;
                }

                if (mail_sent) {
                    Console.WriteLine("---");
                    SendRset(send);
                }

                SendFrom(send, settings.FromAddress);
                SendRcptTo(send, settings.ToAddress);
                SendData(send);
                SendRawBytes(stream, file, settings.UpdateDate, settings.UpdateMessageId);
                SendCrlfDot(send);
                mail_sent = true;
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

            var null_key = GetNullKey(settings);
            if (null_key != "")
                throw new IOException($"{null_key} key does not exist");
        }

        public static void ProcJsonFile(string json_file) {
            if (!File.Exists(json_file))
                throw new IOException("Json file does not exist");

            var settings = GetSettings(json_file);
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

            foreach (var json_file in args) {
                try {
                    ProcJsonFile(json_file);
                } catch (Exception e) {
                    Console.WriteLine($"{json_file}: {e.Message}");
                }
            }
        }
    }
}
