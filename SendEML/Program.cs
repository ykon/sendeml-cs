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
    using SendCmd = Func<String, String>;
    public class Program {
        public const decimal VERSION = 1.1m;

        public const char LF = '\n';
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
            return Array.IndexOf(file_buf, (byte)LF, offset);
        }

        public static ImmutableList<int> FindAllLfIndices(byte[] file_buf) {
            var indices = ImmutableList.CreateBuilder<int>();
            var i = 0;
            while (true) {
                i = FindLfIndex(file_buf, i);
                if (i == -1)
                    return indices.ToImmutable();

                indices.Add(i);
                i += 1;
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

        static int FindLineIndex(ImmutableList<byte[]> lines, Predicate<byte[]> line_pred) {
            return lines.FindIndex(line_pred);
        }

        public static int FindDateLineIndex(ImmutableList<byte[]> lines) {
            return FindLineIndex(lines, IsDateLine);
        }

        public static int FindMessageIdLineIndex(ImmutableList<byte[]> lines) {
            return FindLineIndex(lines, IsMessageIdLine);
        }

        public static ImmutableList<byte[]> ReplaceRawLines(ImmutableList<byte[]> lines, bool update_date, bool update_message_id) {
            if (!update_date && !update_message_id)
                return lines;

            var reps_lines = lines.ToBuilder();

            void ReplaceLine(bool update, Func<ImmutableList<byte[]>, int> find_line, Func<string> make_line) {
                if (update) {
                    var idx = find_line(lines);
                    if (idx != -1)
                        reps_lines[idx] = Encoding.UTF8.GetBytes(make_line());
                }
            }

            ReplaceLine(update_date, FindDateLineIndex, MakeNowDateLine);
            ReplaceLine(update_message_id, FindMessageIdLineIndex, MakeRandomMessageIdLine);

            return reps_lines.ToImmutable();
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

        public static byte[] ReplaceRawBytes(byte[] file_buf, bool update_date, bool update_message_id) {
            return ConcatRawLines(ReplaceRawLines(GetRawLines(file_buf), update_date, update_message_id));
        }

        static volatile bool useParallel = false;

        public static String GetCurrentIdPrefix() {
            return useParallel ? $"id: {Task.CurrentId}, " : "";
        }

        public static void SendRawBytes(Stream stream, string file, bool update_date, bool update_message_id) {
            var path = Path.GetFullPath(file);
            Console.WriteLine(GetCurrentIdPrefix() + $"send: {path}");

            var buf = ReplaceRawBytes(File.ReadAllBytes(path), update_date, update_message_id);
            stream.Write(buf, 0, buf.Length);
            stream.Flush();
        }

        public class Settings {
            public string SmtpHost { get; set; }
            public int SmtpPort { get; set; }
            public string FromAddress { get; set; }
            public ImmutableList<string> ToAddress { get; set; }
            public ImmutableList<string> EmlFile { get; set; }
            public bool UpdateDate { get; set; }
            public bool UpdateMessageId { get; set; }
            public bool UseParallel { get; set; }
        }

        public static Settings GetSettings(string file) {
            var path = Path.GetFullPath(file);
            var options = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), options);
        }

        static readonly Regex LAST_REPLY_REGEX = new Regex(@"^\d{3} .+", RegexOptions.Compiled);

        public static bool IsLastReply(string line) {
            return LAST_REPLY_REGEX.IsMatch(line);
        }

        public static bool IsPositiveReply(string line) {
            return (new[] {'2', '3'}).Contains(line.FirstOrDefault());
        }

        public static string RecvLine(StreamReader reader) {
            while (true) {
                var line = reader.ReadLine()?.Trim() ?? throw new IOException("Connection closed by foreign host.");
                Console.WriteLine(GetCurrentIdPrefix() + $"recv: {line}");

                if (IsLastReply(line)) {
                    if (IsPositiveReply(line))
                        return line;

                    throw new IOException(line);
                }
            }
        }

        public static void SendLine(StreamWriter writer, string cmd) {
            Console.WriteLine(GetCurrentIdPrefix() + "send: " + ((cmd == $"{CRLF}.") ? "<CRLF>." : cmd));

            writer.WriteLine(cmd);
            writer.Flush();
        }

        public static SendCmd MakeSendCmd(StreamReader reader, StreamWriter writer) {
            return cmd => {
                SendLine(writer, cmd);
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

        public static void SendCrLfDot(SendCmd send) {
            send($"{CRLF}.");
        }

        public static void SendQuit(SendCmd send) {
            send("QUIT");
        }

        public static void SendRset(SendCmd send) {
            send("RSET");
        }

        public static void SendMessages(Settings settings, ImmutableList<string> eml_files) {
            using var socket = new TcpClient(settings.SmtpHost, settings.SmtpPort);
            var stream = socket.GetStream();
            stream.ReadTimeout = 1000;

            var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream) {
                NewLine = CRLF
            };
            var send = MakeSendCmd(reader, writer);
            RecvLine(reader);
            SendHello(send);

            var mail_sent = false;
            foreach (var file in eml_files) {
                if (!File.Exists(file)) {
                    Console.WriteLine($"{file}: EML file does not exists");
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
                SendCrLfDot(send);
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

        static void Main(string[] args) {
            if (!args.Any()) {
                WriteUsage();
                return;
            }

            if (args[0] == "--version") {
                WriteVersion();
                return;
            }

            foreach (var json_file in args) {
                if (!File.Exists(json_file)) {
                    Console.WriteLine($"{json_file}: Json file does not exists");
                    continue;
                }

                try {
                    var settings = GetSettings(json_file);
                    if (settings.UseParallel) {
                        useParallel = true;
                        settings.EmlFile.AsParallel().ForAll(f => SendOneMessage(settings, f));
                    } else {
                        SendMessages(settings, settings.EmlFile);
                    }
                } catch (Exception e) {
                    Console.WriteLine($"{json_file}: {e.Message}");
                }
            }
        }
    }
}
