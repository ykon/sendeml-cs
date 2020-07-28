/**
 * Copyright (c) Yuki Ono.
 * Licensed under the MIT License.
 */

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace SendEML.Tests {
    using SendCmd = Func<String, String>;

    [TestClass()]
    public class ProgramTests {
        [TestMethod()]
        public void IsDateLineTest() {
            Assert.IsTrue(Program.IsDateLine("Date: xxx"));
            Assert.IsFalse(Program.IsDateLine("xxx: Date"));
            Assert.IsFalse(Program.IsDateLine("X-Date: xxx"));
        }

        [TestMethod()]
        public void MakeNowDateLineTest() {
            var line = Program.MakeNowDateLine();
            Assert.IsTrue(line.StartsWith("Date: "));
            Assert.IsTrue(line.Length <= 76);
        }

        [TestMethod()]
        public void IsMessageIdLineTest() {
            Assert.IsTrue(Program.IsMessageIdLine("Message-ID: xxx"));
            Assert.IsFalse(Program.IsMessageIdLine("xxx: Message-ID"));
            Assert.IsFalse(Program.IsMessageIdLine("X-Message-ID: xxx"));
        }

        [TestMethod()]
        public void MakeRandomMessageIdLineTest() {
            var line = Program.MakeRandomMessageIdLine();
            Assert.IsTrue(line.StartsWith("Message-ID: "));
            Assert.IsTrue(line.Length <= 76);
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

            Assert.AreEqual(390, indices[indices.Count - 3]);
            Assert.AreEqual(415, indices[indices.Count - 2]);
            Assert.AreEqual(417, indices[indices.Count - 1]);
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

            Assert.AreEqual("Content-Language: en-US\r\n", Encoding.UTF8.GetString(lines[lines.Count - 3]));
            Assert.AreEqual("\r\n", Encoding.UTF8.GetString(lines[lines.Count - 2]));
            Assert.AreEqual("test", Encoding.UTF8.GetString(lines[lines.Count - 1]));
        }

        byte[] MakeByteArray(params char[] cs) {
            return cs.Select(c => (byte)c).ToArray();
        }

        [TestMethod()]
        public void IsFirstDTest() {
            Assert.IsTrue(Program.IsFirstD(MakeByteArray('D', 'A', 'B')));
            Assert.IsTrue(Program.IsFirstD(MakeByteArray('D', 'B', 'A')));
            Assert.IsFalse(Program.IsFirstD(MakeByteArray('A', 'B', 'D')));
            Assert.IsFalse(Program.IsFirstD(MakeByteArray('B', 'A', 'D')));
        }

        [TestMethod()]
        public void IsFirstMTest() {
            Assert.IsTrue(Program.IsFirstM(MakeByteArray('M', 'A', 'B')));
            Assert.IsTrue(Program.IsFirstM(MakeByteArray('M', 'B', 'A')));
            Assert.IsFalse(Program.IsFirstM(MakeByteArray('A', 'B', 'M')));
            Assert.IsFalse(Program.IsFirstM(MakeByteArray('B', 'A', 'M')));
        }

        [TestMethod()]
        public void FindDateLineIndexTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var lines = Program.GetRawLines(mail);
            Assert.AreEqual(4, Program.FindDateLineIndex(lines));

            lines.RemoveAt(4);
            Assert.AreEqual(-1, Program.FindDateLineIndex(lines));
        }

        [TestMethod()]
        public void FindMessageIdLineIndexTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var lines = Program.GetRawLines(mail);
            Assert.AreEqual(3, Program.FindMessageIdLineIndex(lines));

            lines.RemoveAt(3);
            Assert.AreEqual(-1, Program.FindMessageIdLineIndex(lines));
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
        public void ReplaceRawBytesTest() {
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            var repl_mail_noupdate = Program.ReplaceRawBytes(mail, false, false);
            Assert.AreNotEqual(mail, repl_mail_noupdate);
            Assert.IsTrue(mail.SequenceEqual(repl_mail_noupdate));

            var repl_mail = Program.ReplaceRawBytes(mail, true, true);
            Assert.AreNotEqual(mail, repl_mail);
            Assert.IsFalse(mail.SequenceEqual(repl_mail));
        }

        [TestMethod()]
        public void GetSettingsTest() {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, Program.MakeJsonSample());

            var settings = Program.GetSettings(path);
            Assert.AreEqual("172.16.3.151", settings.SmtpHost);
            Assert.AreEqual(25, settings.SmtpPort);
            Assert.AreEqual("a001@ah62.example.jp", settings.FromAddress);
            Assert.IsTrue(new List<string> { "a001@ah62.example.jp", "a002@ah62.example.jp", "a003@ah62.example.jp" }
                .SequenceEqual(settings.ToAddress));
            Assert.IsTrue(new List<string> { "test1.eml", "test2.eml", "test3.eml" }
                .SequenceEqual(settings.EmlFile));
            Assert.AreEqual(true, settings.UpdateDate);
            Assert.AreEqual(true, settings.UpdateMessageId);
            Assert.AreEqual(false, settings.UseParallel);
        }

        [TestMethod()]
        public void IsSuccessTest() {
            Assert.IsTrue(Program.IsSuccess("200 xxx"));
            Assert.IsTrue(Program.IsSuccess("300 xxx"));
            Assert.IsFalse(Program.IsSuccess("400 xxx"));
            Assert.IsFalse(Program.IsSuccess("500 xxx"));
            Assert.IsFalse(Program.IsSuccess("xxx 200"));
            Assert.IsFalse(Program.IsSuccess("xxx 300"));
        }

        public void UseSetStdout(Action block) {
            var orig_console = Console.Out;
            try {
                block();
            } finally {
                Console.SetOut(orig_console);
            }
        }

        [TestMethod()]
        public void SendRawBytesTest() {
            var path = Path.GetTempFileName();
            var mail = Encoding.UTF8.GetBytes(MakeSimpleMail());
            File.WriteAllBytes(path, mail);

            UseSetStdout(() => {
                var str_builder = new StringBuilder();
                Console.SetOut(new StringWriter(str_builder));

                var mem_stream = new MemoryStream();
                Program.SendRawBytes(mem_stream, path, false, false);
                Assert.AreEqual($"send: {path}\r\n", str_builder.ToString());
                Assert.IsTrue(mail.SequenceEqual(mem_stream.ToArray()));
            });

            var mem_stream2 = new MemoryStream();
            Program.SendRawBytes(mem_stream2, path, true, true);
            Assert.IsFalse(mail.SequenceEqual(mem_stream2.ToArray()));
        }

        [TestMethod()]
        public void RecvLineTest() {
            UseSetStdout(() => {
                var str_builder = new StringBuilder();
                Console.SetOut(new StringWriter(str_builder));

                var stream_reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("250 OK\r\n")));
                Assert.AreEqual("250 OK", Program.RecvLine(stream_reader));
                Assert.AreEqual("recv: 250 OK\r\n", str_builder.ToString());
            });

            var stream_reader2 = new StreamReader(new MemoryStream());
            Assert.ThrowsException<IOException>(() => Program.RecvLine(stream_reader2));

            var stream_reader3 = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("554 Transaction failed\r\n")));
            Assert.ThrowsException<IOException>(() => Program.RecvLine(stream_reader3));
        }

        [TestMethod()]
        public void SendLineTest() {
            void test(string cmd, string stdout_expected, string writer_expected) {
                var str_builder = new StringBuilder();
                Console.SetOut(new StringWriter(str_builder));

                var mem_stream = new MemoryStream();
                var stream_writer = new StreamWriter(mem_stream);

                Program.SendLine(stream_writer, cmd);
                Assert.AreEqual(stdout_expected, str_builder.ToString());
                Assert.AreEqual(writer_expected, Encoding.UTF8.GetString(mem_stream.ToArray()));
            }

            UseSetStdout(() => {
                test("EHLO localhost", "send: EHLO localhost\r\n", "EHLO localhost\r\n");
                test("\r\n.", "send: <CRLF>.\r\n", "\r\n.\r\n");
            });
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

            Program.SendRcptTo(test, new List<string> { "a001@ah62.example.jp", "a002@ah62.example.jp", "a003@ah62.example.jp" });
        }

        [TestMethod()]
        public void SendDataTest() {
            Program.SendData(MakeTestSendCmd("DATA"));
        }

        [TestMethod()]
        public void SendCrLfDotTest() {
            Program.SendCrLfDot(MakeTestSendCmd("\r\n."));
        }

        [TestMethod()]
        public void SendQuitTest() {
            Program.SendQuit(MakeTestSendCmd("QUIT"));
        }

        [TestMethod()]
        public void SendRsetTest() {
            Program.SendRset(MakeTestSendCmd("RSET"));
        }
    }
}