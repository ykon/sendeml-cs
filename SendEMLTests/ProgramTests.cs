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

        byte[] MakeSimpleMailBytes() {
            return Encoding.UTF8.GetBytes(MakeSimpleMail());
        }

        string MakeInvalidMail() {
            return MakeSimpleMail().Replace("\r\n\r\n", "");
        }

        byte[] MakeInvalidMailBytes() {
            return Encoding.UTF8.GetBytes(MakeInvalidMail());
        }

        string GetMessageIdLine(byte[] header) {
            var headerStr = Encoding.UTF8.GetString(header);
            return Regex.Match(headerStr, @"Message-ID: [\S]+\r\n").Value;
        }

        string GetDateLine(byte[] header) {
            var headerStr = Encoding.UTF8.GetString(header);
            return Regex.Match(headerStr, @"Date: [\S ]+\r\n").Value;
        }

        [TestMethod()]
        public void GetMessageIdLineTest() {
            var mail = MakeSimpleMailBytes();
            var (header, _) = Program.SplitMail(mail).Value;
            Assert.AreEqual("Message-ID: <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>\r\n", GetMessageIdLine(header));
        }

        [TestMethod()]
        public void GetDateLineTest() {
            var mail = MakeSimpleMailBytes();
            var (header, _) = Program.SplitMail(mail).Value;
            Assert.AreEqual("Date: Sun, 26 Jul 2020 22:01:37 +0900\r\n", GetDateLine(header));
        }

        [TestMethod()]
        public void FindCrIndexTest() {
            var mail = MakeSimpleMailBytes();
            Assert.AreEqual(33, Program.FindCrIndex(mail, 0));
            Assert.AreEqual(48, Program.FindCrIndex(mail, 34));
            Assert.AreEqual(74, Program.FindCrIndex(mail, 58));
        }

        [TestMethod()]
        public void FindLfIndexTest() {
            var mail = MakeSimpleMailBytes();
            Assert.AreEqual(34, Program.FindLfIndex(mail, 0));
            Assert.AreEqual(49, Program.FindLfIndex(mail, 35));
            Assert.AreEqual(75, Program.FindLfIndex(mail, 59));
        }

        [TestMethod()]
        public void FindAllLfIndicesTest() {
            var mail = MakeSimpleMailBytes();
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
            var mail = MakeSimpleMailBytes();

            var buf = Program.CopyNew(mail, 0, 10);
            Assert.AreEqual(10, buf.Length);
            CollectionAssert.AreEqual(mail.Take(10).ToArray(), buf);

            var buf2 = Program.CopyNew(mail, 10, 20);
            Assert.AreEqual(20, buf2.Length);
            CollectionAssert.AreEqual(mail.Skip(10).Take(20).ToArray(), buf2);

            buf[0] = 0;
            Assert.AreEqual(0, buf[0]);
            Assert.AreNotEqual(0, mail[0]);
        }

        [TestMethod()]
        public void GetRawLinesTest() {
            var mail = MakeSimpleMailBytes();
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
        public void ReplaceHeaderTest() {
            var (header, _) = Program.SplitMail(MakeSimpleMailBytes()).Value;
            var dateLine = GetDateLine(header);
            var midLine = GetMessageIdLine(header);

            var replHeaderNoupdate = Program.ReplaceHeader(header, false, false);
            CollectionAssert.AreEqual(header, replHeaderNoupdate);

            var replHeader = Program.ReplaceHeader(header, true, true);
            CollectionAssert.AreNotEqual(header, replHeader);

            (string, string) Replace(bool updateDate, bool updateMessageId) {
                var rHeader = Program.ReplaceHeader(header, updateDate, updateMessageId);
                CollectionAssert.AreNotEqual(header, rHeader);
                return (GetDateLine(rHeader), GetMessageIdLine(rHeader));
            }

            var (rDateLine, rMidLine) = Replace(true, true);
            Assert.AreNotEqual(dateLine, rDateLine);
            Assert.AreNotEqual(midLine, rMidLine);

            var (rDateLine2, rMidLine2) = Replace(true, false);
            Assert.AreNotEqual(dateLine, rDateLine2);
            Assert.AreEqual(midLine, rMidLine2);

            var (rDateLine3, rMidLine3) = Replace(false, true);
            Assert.AreEqual(dateLine, rDateLine3);
            Assert.AreNotEqual(midLine, rMidLine3);
        }

        [TestMethod()]
        public void ConcatBytesTest() {
            var mail = MakeSimpleMailBytes();
            var lines = Program.GetRawLines(mail);

            var newMail = Program.ConcatBytes(lines);
            CollectionAssert.AreEqual(mail, newMail);
        }

        [TestMethod()]
        public void CombineMailTest() {
            var mail = MakeSimpleMailBytes();
            var (header, body) = Program.SplitMail(mail).Value;
            var newMail = Program.CombineMail(header, body);
            CollectionAssert.AreEqual(mail, newMail);
        }

        [TestMethod()]
        public void FindEmptyLineTest() {
            var mail = MakeSimpleMailBytes();
            Assert.AreEqual(414, Program.FindEmptyLine(mail));

            var invalidMail = MakeInvalidMailBytes();
            Assert.AreEqual(-1, Program.FindEmptyLine(invalidMail));
        }

        [TestMethod()]
        public void SplitMail() {
            var mail = MakeSimpleMailBytes();
            var headerBody = Program.SplitMail(mail);
            Assert.IsTrue(headerBody.HasValue);

            var (header, body) = headerBody.Value;
            CollectionAssert.AreEqual(mail.Take(414).ToArray(), header);
            CollectionAssert.AreEqual(mail.Skip(414 + 4).ToArray(), body);

            var invalidMail = MakeInvalidMailBytes();
            Assert.IsFalse(Program.SplitMail(invalidMail).HasValue);
        }

        [TestMethod()]
        public void ReplaceRawBytesTest() {
            var mail = MakeSimpleMailBytes();
            var replMailNoupdate = Program.ReplaceRawBytes(mail, false, false);
            Assert.AreEqual(mail, replMailNoupdate);

            var replMail = Program.ReplaceRawBytes(mail, true, true);
            Assert.AreNotEqual(mail, replMail);
            CollectionAssert.AreNotEqual(mail, replMail);
            CollectionAssert.AreEqual(mail[^100..^0], replMail[^100..^0]);

            var invalidMail = MakeInvalidMailBytes();
            Assert.ThrowsException<IOException>(() => Program.ReplaceRawBytes(invalidMail, true, true));
        }

        [TestMethod()]
        public void GetSettingsTest() {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, Program.MakeJsonSample());

            var settings = Program.GetSettings(path);
            Assert.AreEqual("172.16.3.151", settings.SmtpHost);
            Assert.AreEqual(25, settings.SmtpPort);
            Assert.AreEqual("a001@ah62.example.jp", settings.FromAddress);
            CollectionAssert.AreEqual(new[] { "a001@ah62.example.jp", "a002@ah62.example.jp", "a003@ah62.example.jp" },
                settings.ToAddress);
            CollectionAssert.AreEqual(new[] { "test1.eml", "test2.eml", "test3.eml" },
                settings.EmlFile);
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
            var origConsole = Console.Out;
            try {
                block();
            } finally {
                Console.SetOut(origConsole);
            }
        }

        string GetStdout(Action block) {
            var strBuilder = new StringBuilder();
            UseSetStdout(() => {
                Console.SetOut(new StringWriter(strBuilder));
                block();
            });
            return strBuilder.ToString();
        }

        [TestMethod()]
        public void SendRawBytesTest() {
            var path = Path.GetTempFileName();
            var mail = MakeSimpleMailBytes();
            File.WriteAllBytes(path, mail);

            var memStream = new MemoryStream();
            var sendLine = GetStdout(() => {
                Program.SendRawBytes(memStream, path, false, false);
            });
            Assert.AreEqual($"send: {path}\r\n", sendLine);
            CollectionAssert.AreEqual(mail, memStream.ToArray());

            var memStream2 = new MemoryStream();
            Program.SendRawBytes(memStream2, path, true, true);
            CollectionAssert.AreNotEqual(mail, memStream2.ToArray());
        }

        [TestMethod()]
        public void RecvLineTest() {
            var recvLine = GetStdout(() => {
                var streamReader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("250 OK\r\n")));
                Assert.AreEqual("250 OK", Program.RecvLine(streamReader));
            });
            Assert.AreEqual("recv: 250 OK\r\n", recvLine);

            var streamReader2 = new StreamReader(new MemoryStream());
            Assert.ThrowsException<IOException>(() => Program.RecvLine(streamReader2));

            var streamReader3 = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("554 Transaction failed\r\n")));
            Assert.ThrowsException<IOException>(() => Program.RecvLine(streamReader3));
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
            var memStream = new MemoryStream();
            var sendLine = GetStdout(() => {
                Program.SendLine(memStream, "EHLO localhost");
            });
            Assert.AreEqual("send: EHLO localhost\r\n", sendLine);
            Assert.AreEqual("EHLO localhost\r\n", Encoding.UTF8.GetString(memStream.ToArray()));
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