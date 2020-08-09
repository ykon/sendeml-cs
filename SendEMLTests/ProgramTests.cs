/**
 * Copyright (c) Yuki Ono.
 * Licensed under the MIT License.
 */

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SendEML.Tests {
    using SendCmd = Func<string, string>;

    [TestClass()]
    public class ProgramTests {
        [TestMethod()]
        public void MatchHeaderFieldTest() {
            Func<string, string, bool> test =
                (s1, s2) => Program.MatchHeaderField(Encoding.UTF8.GetBytes(s1), Encoding.UTF8.GetBytes(s2));

            Assert.IsTrue(test("Test:", "Test:"));
            Assert.IsTrue(test("Test: ", "Test:"));
            Assert.IsTrue(test("Test:x", "Test:"));

            Assert.IsFalse(test("", "Test:"));
            Assert.IsFalse(test("T", "Test:"));
            Assert.IsFalse(test("Test", "Test:"));
        }

        [TestMethod()]
        public void IsDateLineTest() {
            Func<string, bool> test = s => Program.IsDateLine(Encoding.UTF8.GetBytes(s));

            Assert.IsTrue(test("Date: xxx"));
            Assert.IsTrue(test("Date:xxx"));
            Assert.IsTrue(test("Date:"));
            Assert.IsTrue(test("Date:   "));

            Assert.IsFalse(test(""));
            Assert.IsFalse(test("Date"));
            Assert.IsFalse(test("xxx: Date"));
            Assert.IsFalse(test("X-Date: xxx"));
        }

        [TestMethod()]
        public void MakeNowDateLineTest() {
            var line = Program.MakeNowDateLine();
            Assert.IsTrue(line.StartsWith("Date: "));
            Assert.IsTrue(line.EndsWith(Program.CRLF));
            Assert.IsTrue(line.Length <= 80);
        }

        [TestMethod()]
        public void IsMessageIdLineTest() {
            Func<string, bool> test = s => Program.IsMessageIdLine(Encoding.UTF8.GetBytes(s));

            Assert.IsTrue(test("Message-ID: xxx"));
            Assert.IsTrue(test("Message-ID:xxx"));
            Assert.IsTrue(test("Message-ID:"));
            Assert.IsTrue(test("Message-ID:   "));

            Assert.IsFalse(test(""));
            Assert.IsFalse(test("Message-ID"));
            Assert.IsFalse(test("xxx: Message-ID"));
            Assert.IsFalse(test("X-Message-ID: xxx"));
        }

        [TestMethod()]
        public void MakeRandomMessageIdLineTest() {
            var line = Program.MakeRandomMessageIdLine();
            Assert.IsTrue(line.StartsWith("Message-ID: "));
            Assert.IsTrue(line.EndsWith(Program.CRLF));
            Assert.IsTrue(line.Length <= 80);
        }

        string MakeSimpleMail() {
            return @"From: a001 <a001@ah62.example.jp>
Subject: test
To: a002@ah62.example.jp
Message-ID: <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>
Date: Sun, 26 Jul 2020 22:01:37 +0900
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:78.0) Gecko/20100101
 Thunderbird/78.0.1
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8; format=flowed
Content-Transfer-Encoding: 7bit
Content-Language: en-US

test";
        }

        [TestMethod()]
        public void FindLfIndexTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            Assert.AreEqual(34, Program.FindLfIndex(mail, 0));
            Assert.AreEqual(49, Program.FindLfIndex(mail, 35));
            Assert.AreEqual(75, Program.FindLfIndex(mail, 59));
        }

        [TestMethod()]
        public void FindAllLfIndicesTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var indices = Program.FindAllLfIndices(mail);

            Assert.AreEqual(34, indices[0]);
            Assert.AreEqual(49, indices[1]);
            Assert.AreEqual(75, indices[2]);

            Assert.AreEqual(390, indices[^3]);
            Assert.AreEqual(415, indices[^2]);
            Assert.AreEqual(417, indices[^1]);
        }

        [TestMethod()]
        public void CopyNewTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());

            var buf = Program.CopyNew(mail, 0, 10);
            Assert.AreEqual(10, buf.Length);
            Assert.IsTrue(mail.Take(10).SequenceEqual(buf));

            var buf2 = Program.CopyNew(mail, 10, 20);
            Assert.AreEqual(20, buf2.Length);
            Assert.IsTrue(mail.Skip(10).Take(20).SequenceEqual(buf2));

            buf[0] = 0;
            Assert.AreEqual(0, buf[0]);
            Assert.AreNotEqual(0, mail[0]);
        }

        [TestMethod()]
        public void GetRawLinesTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var lines = Program.GetRawLines(mail);

            Assert.AreEqual(13, lines.Count);
            Assert.AreEqual("From: a001 <a001@ah62.example.jp>\r\n", Encoding.UTF8.GetString(lines[0]));
            Assert.AreEqual("Subject: test\r\n", Encoding.UTF8.GetString(lines[1]));
            Assert.AreEqual("To: a002@ah62.example.jp\r\n", Encoding.UTF8.GetString(lines[2]));

            Assert.AreEqual("Content-Language: en-US\r\n", Encoding.UTF8.GetString(lines[^3]));
            Assert.AreEqual("\r\n", Encoding.UTF8.GetString(lines[^2]));
            Assert.AreEqual("test", Encoding.UTF8.GetString(lines[^1]));
        }

        [TestMethod()]
        public void IsNotUpdateTest() {
            Assert.IsFalse(Program.IsNotUpdate(true, true));
            Assert.IsFalse(Program.IsNotUpdate(true, false));
            Assert.IsFalse(Program.IsNotUpdate(false, true));
            Assert.IsTrue(Program.IsNotUpdate(false, false));
        }

        [TestMethod()]
        public void ReplaceRawLinesTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var lines = Program.GetRawLines(mail);

            var repl_lines_noupdate = Program.ReplaceRawLines(lines, false, false);
            Assert.AreEqual(lines, repl_lines_noupdate);

            var repl_lines = Program.ReplaceRawLines(lines, true, true);
            foreach (var i in Enumerable.Range(0, lines.Count).Where(n => n < 3 || n > 4))
                Assert.AreEqual(lines[i], repl_lines[i]);

            Assert.AreNotEqual(lines[3], repl_lines[3]);
            Assert.AreNotEqual(lines[4], repl_lines[4]);

            Assert.IsTrue(Encoding.UTF8.GetString(lines[3]).StartsWith("Message-ID: "));
            Assert.IsTrue(Encoding.UTF8.GetString(repl_lines[3]).StartsWith("Message-ID: "));
            Assert.IsTrue(Encoding.UTF8.GetString(lines[4]).StartsWith("Date: "));
            Assert.IsTrue(Encoding.UTF8.GetString(repl_lines[4]).StartsWith("Date: "));
        }

        [TestMethod()]
        public void ConcatRawLinesTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var lines = Program.GetRawLines(mail);

            var new_mail = Program.ConcatRawLines(lines);
            Assert.IsTrue(mail.SequenceEqual(new_mail));
        }

        [TestMethod()]
        public void CombineMailTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var (header, body) = Program.SplitMail(mail).Value;
            var new_mail = Program.CombineMail(header, body);

            Assert.IsTrue(mail.SequenceEqual(new_mail));
        }

        [TestMethod()]
        public void FindEmptyLineTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            Assert.AreEqual(414, Program.FindEmptyLine(mail));

            var invalid_mail = Encoding.UTF8.GetBytes(MakeSimpleMail().Replace("\r\n\r\n", ""));
            Assert.AreEqual(-1, Program.FindEmptyLine(invalid_mail));
        }

        [TestMethod()]
        public void SplitMail() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var header_body = Program.SplitMail(mail);
            Assert.IsTrue(header_body.HasValue);

            var (header, body) = header_body.Value;
            Assert.IsTrue(mail.Take(414).SequenceEqual(header));
            Assert.IsTrue(mail.Skip(414 + 4).Take(4).SequenceEqual(body));

            var invalid_mail = Encoding.UTF8.GetBytes(MakeSimpleMail().Replace("\r\n\r\n", ""));
            Assert.IsFalse(Program.SplitMail(invalid_mail).HasValue);
        }

        [TestMethod()]
        public void ReplaceRawBytesTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var repl_mail_noupdate = Program.ReplaceRawBytes(mail, false, false);
            Assert.AreEqual(mail, repl_mail_noupdate);

            var repl_mail = Program.ReplaceRawBytes(mail, true, true);
            Assert.AreNotEqual(mail, repl_mail);
            Assert.IsFalse(mail.SequenceEqual(repl_mail));
            Assert.IsTrue(mail[^100..^0].SequenceEqual(repl_mail[^100..^0]));

            var invalid_mail = Encoding.UTF8.GetBytes(MakeSimpleMail().Replace("\r\n\r\n", ""));
            Assert.ThrowsException<IOException>(() => Program.ReplaceRawBytes(invalid_mail, true, true));
        }

        [TestMethod()]
        public void GetSettingsTest() {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, Program.MakeJsonSample());

            var settings = Program.GetSettings(path);
            Assert.AreEqual("172.16.3.151", settings.SmtpHost);
            Assert.AreEqual(25, settings.SmtpPort);
            Assert.AreEqual("a001@ah62.example.jp", settings.FromAddress);
            Assert.IsTrue(new[] { "a001@ah62.example.jp", "a002@ah62.example.jp", "a003@ah62.example.jp" }
                .SequenceEqual(settings.ToAddress));
            Assert.IsTrue(new[] { "test1.eml", "test2.eml", "test3.eml" }
                .SequenceEqual(settings.EmlFile));
            Assert.AreEqual(true, settings.UpdateDate);
            Assert.AreEqual(true, settings.UpdateMessageId);
            Assert.AreEqual(false, settings.UseParallel);
        }

        [TestMethod()]
        public void IsLastReplyTest() {
            Assert.IsFalse(Program.IsLastReply("250-First line"));
            Assert.IsFalse(Program.IsLastReply("250-Second line"));
            Assert.IsFalse(Program.IsLastReply("250-234 Text beginning with numbers"));
            Assert.IsTrue(Program.IsLastReply("250 The last line"));
        }

        [TestMethod()]
        public void IsPositiveReplyTest() {
            Assert.IsTrue(Program.IsPositiveReply("200 xxx"));
            Assert.IsTrue(Program.IsPositiveReply("300 xxx"));
            Assert.IsFalse(Program.IsPositiveReply("400 xxx"));
            Assert.IsFalse(Program.IsPositiveReply("500 xxx"));
            Assert.IsFalse(Program.IsPositiveReply("xxx 200"));
            Assert.IsFalse(Program.IsPositiveReply("xxx 300"));
        }

        void UseSetStdout(Action block) {
            var orig_console = Console.Out;
            try {
                block();
            } finally {
                Console.SetOut(orig_console);
            }
        }

        string GetStdout(Action block) {
            var str_builder = new StringBuilder();
            UseSetStdout(() => {
                Console.SetOut(new StringWriter(str_builder));
                block();
            });
            return str_builder.ToString();
        }

        [TestMethod()]
        public void SendRawBytesTest() {
            var path = Path.GetTempFileName();
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            File.WriteAllBytes(path, mail);

            var mem_stream = new MemoryStream();
            var send_line = GetStdout(() => {
                Program.SendRawBytes(mem_stream, path, false, false);
            });
            Assert.AreEqual($"send: {path}\r\n", send_line);
            Assert.IsTrue(mail.SequenceEqual(mem_stream.ToArray()));

            var mem_stream2 = new MemoryStream();
            Program.SendRawBytes(mem_stream2, path, true, true);
            Assert.IsFalse(mail.SequenceEqual(mem_stream2.ToArray()));
        }

        [TestMethod()]
        public void RecvLineTest() {
            var recv_line = GetStdout(() => {
                var stream_reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("250 OK\r\n")));
                Assert.AreEqual("250 OK", Program.RecvLine(stream_reader));
            });
            Assert.AreEqual("recv: 250 OK\r\n", recv_line);

            var stream_reader2 = new StreamReader(new MemoryStream());
            Assert.ThrowsException<IOException>(() => Program.RecvLine(stream_reader2));

            var stream_reader3 = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("554 Transaction failed\r\n")));
            Assert.ThrowsException<IOException>(() => Program.RecvLine(stream_reader3));
        }

        [TestMethod()]
        public void ReplaceCrlfDotTest() {
            Assert.AreEqual("TEST", Program.ReplaceCrlfDot("TEST"));
            Assert.AreEqual("CRLF", Program.ReplaceCrlfDot("CRLF"));
            Assert.AreEqual(Program.CRLF, Program.ReplaceCrlfDot(Program.CRLF));
            Assert.AreEqual(".", Program.ReplaceCrlfDot("."));
            Assert.AreEqual("<CRLF>.", Program.ReplaceCrlfDot($"{Program.CRLF}."));
        }

        [TestMethod()]
        public void SendLineTest() {
            var mem_stream = new MemoryStream();
            var send_line = GetStdout(() => {
                Program.SendLine(mem_stream, "EHLO localhost");
            });
            Assert.AreEqual("send: EHLO localhost\r\n", send_line);
            Assert.AreEqual("EHLO localhost\r\n", Encoding.UTF8.GetString(mem_stream.ToArray()));
        }

        SendCmd MakeTestSendCmd(string expected) {
            return cmd => {
                Assert.AreEqual(expected, cmd);
                return cmd;
            };
        }

        [TestMethod()]
        public void SendHelloTest() {
            Program.SendHello(MakeTestSendCmd("EHLO localhost"));
        }

        [TestMethod()]
        public void SendFromTest() {
            Program.SendFrom(MakeTestSendCmd("MAIL FROM: <a001@ah62.example.jp>"), "a001@ah62.example.jp");
        }

        [TestMethod()]
        public void SendRcptToTest() {
            var count = 1;
            SendCmd test = (string cmd) => {
                Assert.AreEqual($"RCPT TO: <a00{count}@ah62.example.jp>", cmd);
                count += 1;
                return cmd;
            };

            Program.SendRcptTo(test, ImmutableList.Create("a001@ah62.example.jp", "a002@ah62.example.jp", "a003@ah62.example.jp"));
        }

        [TestMethod()]
        public void SendDataTest() {
            Program.SendData(MakeTestSendCmd("DATA"));
        }

        [TestMethod()]
        public void SendCrlfDotTest() {
            Program.SendCrlfDot(MakeTestSendCmd("\r\n."));
        }

        [TestMethod()]
        public void SendQuitTest() {
            Program.SendQuit(MakeTestSendCmd("QUIT"));
        }

        [TestMethod()]
        public void SendRsetTest() {
            Program.SendRset(MakeTestSendCmd("RSET"));
        }

        [TestMethod()]
        public void WriteVersionTest() {
            var version = GetStdout(() => {
                Program.WriteVersion();
            });
            Assert.IsTrue(version.Contains("Version:"));
            Assert.IsTrue(version.Contains(Program.VERSION.ToString()));
        }

        [TestMethod()]
        public void WriteUsageTest() {
            var usage = GetStdout(() => {
                Program.WriteUsage();
            });
            Assert.IsTrue(usage.Contains("Usage:"));
        }

        [TestMethod()]
        public void CheckSettingsTest() {
            static void checkNoKey(string key) {
                var json = Program.MakeJsonSample();
                var noKey = new Regex(key).Replace(json, $"X-{key}", 1);
                Program.CheckSettings(Program.GetSettingsFromText(noKey));
            }

            Assert.ThrowsException<IOException>(() => checkNoKey("smtpHost"));
            Assert.ThrowsException<IOException>(() => checkNoKey("smtpPort"));
            Assert.ThrowsException<IOException>(() => checkNoKey("fromAddress"));
            Assert.ThrowsException<IOException>(() => checkNoKey("toAddress"));
            Assert.ThrowsException<IOException>(() => checkNoKey("emlFile"));

            try {
                checkNoKey("testKey");
            } catch(Exception e) {
                Assert.Fail("Expected no exception: " + e.Message);
            }
        }

        [TestMethod()]
        public void ProcJsonFileTest() {
            Assert.ThrowsException<IOException>(() => Program.ProcJsonFile("__test__"));
        }
    }
}