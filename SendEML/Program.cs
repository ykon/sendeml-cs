/**
 * Copyright (c) Yuki Ono.
 * Licensed under the MIT License.
 */

using System;
using System.Collections.Generic;
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
        public const double VERSION = 1.5;

        public const byte CR = (byte)'\r';
        public const byte LF = (byte)'\n';
        public const byte SPACE = (byte)' ';
        public const byte HTAB = (byte)'\t';
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

        public static byte[] ConcatBytes(IEnumerable<byte[]> bytesList) {
            var buf = new byte[bytesList.Sum(b => b.Length)];
            var offset = 0;
            foreach (var b in bytesList) {
                Buffer.BlockCopy(b, 0, buf, offset, b.Length);
                offset += b.Length;
            }
            return buf;
        }

        public static bool IsWsp(byte b) {
            return b == SPACE || b == HTAB;
        }

        public static bool IsFirstWsp(byte[] bytes) {
            return IsWsp(bytes.FirstOrDefault());
        }

        public static byte[] ReplaceHeader(byte[] header, bool updateDate, bool updateMessageId) {
            if (IsNotUpdate(updateDate, updateMessageId))
                return header;

            static void RemoveFolding(ImmutableList<byte[]>.Builder lines, int idx) {
                for (var i = idx; i < lines.Count; i++) {
                    if (IsFirstWsp(lines[i]))
                        lines[i] = new byte[0];
                    else
                        break;
                }
            }

            static void ReplaceLine(ImmutableList<byte[]>.Builder lines, bool update, Predicate<byte[]> matchLine, Func<string> makeLine) {
                if (update) {
                    var idx = lines.FindIndex(matchLine);
                    if (idx != -1) {
                        lines[idx] = Encoding.UTF8.GetBytes(makeLine());
                        RemoveFolding(lines, idx + 1);
                    }
                }
            }

            var lines = GetRawLines(header).ToBuilder();
            ReplaceLine(lines, updateDate, IsDateLine, MakeNowDateLine);
            ReplaceLine(lines, updateMessageId, IsMessageIdLine, MakeRandomMessageIdLine);
            return ConcatBytes(lines.ToImmutable());
        }

        public static readonly byte[] EMPTY_LINE = new[] { CR, LF, CR, LF };

        public static byte[] CombineMail(byte[] header, byte[] body) {
            return ConcatBytes(new[] { header, EMPTY_LINE, body });
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

        public static byte[] ReplaceMail(byte[] fileBuf, bool updateDate, bool updateMessageId) {
            if (IsNotUpdate(updateDate, updateMessageId))
                return fileBuf;

            var mail = SplitMail(fileBuf);
            if (!mail.HasValue)
                throw new Exception("Invalid mail");

            var (header, body) = mail.Value;
            var replHeader = ReplaceHeader(header, updateDate, updateMessageId);
            return CombineMail(replHeader, body);
        }

        public static string MakeIdPrefix(bool use_parallel) {
            return use_parallel ? $"id: {Task.CurrentId}, " : "";
        }

        public static void SendMail(Stream stream, string file, bool updateDate, bool updateMessageId, bool use_parallel = false) {
            Console.WriteLine(MakeIdPrefix(use_parallel) + $"send: {file}");

            var path = Path.GetFullPath(file);
            var buf = ReplaceMail(File.ReadAllBytes(path), updateDate, updateMessageId);
            stream.Write(buf, 0, buf.Length);
            stream.Flush();
        }

        public readonly struct Settings {
            public string SmtpHost { get; }
            public int SmtpPort { get; }
            public string FromAddress { get; }
            public ImmutableList<string> ToAddresses { get; }
            public ImmutableList<string> EmlFiles { get; }
            public bool UpdateDate { get; }
            public bool UpdateMessageId { get; }
            public bool UseParallel { get; }

            public Settings(string smtpHost, int smtpPort, string fromAddress,
                ImmutableList<string> toAddresses, ImmutableList<string> emlFiles,
                bool updateDate, bool updateMessageId, bool useParallel) {
                SmtpHost = smtpHost;
                SmtpPort = smtpPort;
                FromAddress = fromAddress;
                ToAddresses = toAddresses;
                EmlFiles = emlFiles;
                UpdateDate = updateDate;
                UpdateMessageId = updateMessageId;
                UseParallel = useParallel;
            }
        }

        public static JsonDocument GetSettingsFromText(string text) {
            var options = new JsonDocumentOptions {
                CommentHandling = JsonCommentHandling.Skip
            };

            return JsonDocument.Parse(text, options);
        }

        public static JsonDocument GetSettings(string file) {
            var path = Path.GetFullPath(file);
            return GetSettingsFromText(File.ReadAllText(path));
        }

        public static Settings MapSettings(JsonDocument json) {
            var root = json.RootElement;

            var updateDate = root.TryGetProperty("updateDate", out var p1) ? p1.GetBoolean() : true;
            var updateMessageId = root.TryGetProperty("updateMessageId", out var p2) ? p2.GetBoolean() : true;
            var useParallel = root.TryGetProperty("useParallel", out var p3) ? p3.GetBoolean() : false;

            return new Settings(
                root.GetProperty("smtpHost").GetString(),
                root.GetProperty("smtpPort").GetInt32(),
                root.GetProperty("fromAddress").GetString(),
                root.GetProperty("toAddresses").EnumerateArray().Select(e => e.GetString()).ToImmutableList(),
                root.GetProperty("emlFiles").EnumerateArray().Select(e => e.GetString()).ToImmutableList(),
                updateDate, updateMessageId, useParallel);
        }

        public static void CheckJsonValue(JsonElement root, string name, JsonValueKind kind) {
            if (root.TryGetProperty(name, out var prop)) {
                if (prop.ValueKind != kind)
                    throw new Exception($"{name}: Invalid type: {prop}");
            }
        }

        public static void CheckJsonArrayValue(JsonElement root, string name, JsonValueKind kind) {
            if (root.TryGetProperty(name, out var prop)) {
                if (prop.ValueKind != JsonValueKind.Array)
                    throw new Exception($"{name}: Invalid type (array): {prop}");

                var elm = prop.EnumerateArray().Where(e => e.ValueKind != kind).Take(1);
                if (elm.Any())
                    throw new Exception($"{name}: Invalid type (element): {elm.First()}");
            }
        }

        public static void CheckJsonBoolValue(JsonElement root, string name) {
            if (root.TryGetProperty(name, out var prop)) {
                if (prop.ValueKind != JsonValueKind.False && prop.ValueKind != JsonValueKind.True)
                    throw new Exception($"{name}: Invalid type: {prop}");
            }
        }

        public static void CheckSettings(JsonDocument json) {
            var root = json.RootElement;
            var names = new[] { "smtpHost", "smtpPort", "fromAddress", "toAddresses", "emlFiles" };
            var key = names.FirstOrDefault(n => !root.TryGetProperty(n, out var _));
            if (key != null)
                throw new Exception($"{key} key does not exist");

            CheckJsonValue(root, "smtpHost", JsonValueKind.String);
            CheckJsonValue(root, "smtpPort", JsonValueKind.Number);
            CheckJsonValue(root, "fromAddress", JsonValueKind.String);
            CheckJsonArrayValue(root, "toAddresses", JsonValueKind.String);
            CheckJsonArrayValue(root, "emlFiles", JsonValueKind.String);
            CheckJsonBoolValue(root, "updateDate");
            CheckJsonBoolValue(root, "updateMessage-Id");
            CheckJsonBoolValue(root, "useParallel");
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

        public static string RecvLine(StreamReader reader, bool use_parallel = false) {
            while (true) {
                var line = reader.ReadLine()?.Trim() ?? throw new Exception("Connection closed by foreign host");
                Console.WriteLine(MakeIdPrefix(use_parallel) + $"recv: {line}");

                if (IsLastReply(line)) {
                    if (IsPositiveReply(line))
                        return line;

                    throw new Exception(line);
                }
            }
        }

        public static string ReplaceCrlfDot(string cmd) {
            return cmd == $"{CRLF}." ? "<CRLF>." : cmd;
        }

        public static void SendLine(Stream output, string cmd, bool use_parallel = false) {
            Console.WriteLine(MakeIdPrefix(use_parallel) + "send: " + ReplaceCrlfDot(cmd));

            var buf = Encoding.UTF8.GetBytes(cmd + CRLF);
            output.Write(buf, 0, buf.Length);
            output.Flush();
        }

        public static SendCmd MakeSendCmd(StreamReader reader, bool use_parallel) {
            return cmd => {
                SendLine(reader.BaseStream, cmd, use_parallel);
                return RecvLine(reader, use_parallel);
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

        public static void SendMessages(Settings settings, ImmutableList<string> emlFiles, bool useParallel) {
            using var socket = new TcpClient(settings.SmtpHost, settings.SmtpPort);
            var stream = socket.GetStream();
            var reader = new StreamReader(stream);
            var send = MakeSendCmd(reader, useParallel);

            RecvLine(reader, useParallel);
            SendHello(send);

            var reset = false;
            foreach (var file in emlFiles) {
                if (!File.Exists(file)) {
                    Console.WriteLine($"{file}: EML file does not exist");
                    continue;
                }

                if (reset) {
                    Console.WriteLine("---");
                    SendRset(send);
                }

                SendFrom(send, settings.FromAddress);
                SendRcptTo(send, settings.ToAddresses);
                SendData(send);

                try {
                    SendMail(stream, file, settings.UpdateDate, settings.UpdateMessageId, useParallel);
                } catch (Exception e) {
                    throw new Exception($"{file}: {e.Message}");
                }

                SendCrlfDot(send);
                reset = true;
            }

            SendQuit(send);
        }

        public static string MakeJsonSample() {
            return @"{
    ""smtpHost"": ""172.16.3.151"",
    ""smtpPort"": 25,
    ""fromAddress"": ""a001@ah62.example.jp"",
    ""toAddresses"": [
        ""a001@ah62.example.jp"",
        ""a002@ah62.example.jp"",
        ""a003@ah62.example.jp""
    ],
    ""emlFiles"": [
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

        public static void ProcJsonFile(string jsonFile) {
            if (!File.Exists(jsonFile))
                throw new Exception("Json file does not exist");

            var json = GetSettings(jsonFile);
            CheckSettings(json);
            var settings = MapSettings(json);

            if (settings.UseParallel && settings.EmlFiles.Count > 1) {
                settings.EmlFiles.AsParallel().ForAll(f =>
                    SendMessages(settings, ImmutableList.Create(f), true)
                );
            } else {
                SendMessages(settings, settings.EmlFiles, false);
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
                    Console.WriteLine($"error: {jsonFile}: {e.Message}");
                }
            }
        }
    }
}
