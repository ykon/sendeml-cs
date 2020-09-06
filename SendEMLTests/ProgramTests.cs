/**
 * Copyright (c) Yuki Ono.
 * Licensed under the MIT License.
 */

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

#nullable enable

namespace SendEML.Tests {
    using SendCmd = Func<string, string>;

    public static class Extensions {
        public static byte[] ToBytes(this string str) {
            return Encoding.UTF8.GetBytes(str);
        }

        public static string ToUtf8String(this byte[] bytes) {
            return Encoding.UTF8.GetString(bytes);
        }

        public static JsonDocument ToJson(this string str) {
            return JsonDocument.Parse(str);
        }
    }

    [TestClass()]
    public class ProgramTests {
        [TestMethod()]
        public void MatchHeaderTest() {
            Func<string, string, bool> test =
                (s1, s2) => Program.MatchHeader(s1.ToBytes(), s2.ToBytes());

            Assert.IsTrue(test("Test:", "Test:"));
            Assert.IsTrue(test("Test:   ", "Test:"));
            Assert.IsTrue(test("Test: xxx", "Test:"));

            Assert.IsFalse(test("", "Test:"));
            Assert.IsFalse(test("T", "Test:"));
            Assert.IsFalse(test("Test", "Test:"));
            Assert.IsFalse(test("Xest:", "Test:"));

            Assert.ThrowsException<Exception>(() => test("Test: xxx", ""));
        }

        [TestMethod()]
        public void IsDateLineTest() {
            Func<string, bool> test = s => Program.IsDateLine(s.ToBytes());

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
            Func<string, bool> test = s => Program.IsMessageIdLine(s.ToBytes());

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

        string MakeSimpleMailText() {
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

        byte[] MakeFoldedMail() {
            var text = @"From: a001 <a001@ah62.example.jp>
Subject: test
To: a002@ah62.example.jp
Message-ID:
 <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>
Date:
 Sun, 26 Jul 2020
 22:01:37 +0900
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:78.0) Gecko/20100101
 Thunderbird/78.0.1
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8; format=flowed
Content-Transfer-Encoding: 7bit
Content-Language: en-US

test";
            return text.ToBytes();
        }

        byte[] MakeFoldedEndDate() {
            var text = @"From: a001 <a001@ah62.example.jp>
Subject: test
To: a002@ah62.example.jp
Message-ID:
 <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:78.0) Gecko/20100101
 Thunderbird/78.0.1
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8; format=flowed
Content-Transfer-Encoding: 7bit
Content-Language: en-US
Date:
 Sun, 26 Jul 2020
 22:01:37 +0900
";
            return text.ToBytes();
        }

        byte[] MakeFoldedEndMessageId() {
            var text = @"From: a001 <a001@ah62.example.jp>
Subject: test
To: a002@ah62.example.jp
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:78.0) Gecko/20100101
 Thunderbird/78.0.1
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8; format=flowed
Content-Transfer-Encoding: 7bit
Content-Language: en-US
Date:
 Sun, 26 Jul 2020
 22:01:37 +0900
Message-ID:
 <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>
";
            return text.ToBytes();
        }

        byte[] MakeSimpleMail() {
            return MakeSimpleMailText().ToBytes();
        }

        byte[] MakeInvalidMail() {
            return MakeSimpleMailText().Replace("\r\n\r\n", "").ToBytes();
        }

        string GetHeaderLine(byte[] header, string name) {
            var headerStr = header.ToUtf8String();
            return Regex.Match(headerStr, name + @":[\s\S]+?\r\n(?=([^ \t]|$))").Value;
        }

        string GetMessageIdLine(byte[] header) {
            return GetHeaderLine(header, "Message-ID");
        }

        string GetDateLine(byte[] header) {
            return GetHeaderLine(header, "Date");
        }

        [TestMethod()]
        public void GetHeaderLineTest() {
            var mail = MakeSimpleMail();
            Assert.AreEqual("Date: Sun, 26 Jul 2020 22:01:37 +0900\r\n", GetDateLine(mail));
            Assert.AreEqual("Message-ID: <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>\r\n", GetMessageIdLine(mail));

            var fMail = MakeFoldedMail();
            Assert.AreEqual("Date:\r\n Sun, 26 Jul 2020\r\n 22:01:37 +0900\r\n", GetDateLine(fMail));
            Assert.AreEqual("Message-ID:\r\n <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>\r\n", GetMessageIdLine(fMail));

            var eDate = MakeFoldedEndDate();
            Assert.AreEqual("Date:\r\n Sun, 26 Jul 2020\r\n 22:01:37 +0900\r\n", GetDateLine(eDate));

            var eMessageId = MakeFoldedEndMessageId();
            Assert.AreEqual("Message-ID:\r\n <b0e564a5-4f70-761a-e103-70119d1bcb32@ah62.example.jp>\r\n", GetMessageIdLine(eMessageId));
        }

        [TestMethod()]
        public void FindCrTest() {
            var mail = MakeSimpleMail();
            Assert.AreEqual(33, Program.FindCr(mail, 0));
            Assert.AreEqual(48, Program.FindCr(mail, 34));
            Assert.AreEqual(74, Program.FindCr(mail, 58));
        }

        [TestMethod()]
        public void FindLfTest() {
            var mail = MakeSimpleMail();
            Assert.AreEqual(34, Program.FindLf(mail, 0));
            Assert.AreEqual(49, Program.FindLf(mail, 35));
            Assert.AreEqual(75, Program.FindLf(mail, 59));
        }

        [TestMethod()]
        public void FindAllLfTest() {
            var mail = MakeSimpleMail();
            var indices = Program.FindAllLf(mail);

            Assert.AreEqual(34, indices[0]);
            Assert.AreEqual(49, indices[1]);
            Assert.AreEqual(75, indices[2]);

            Assert.AreEqual(390, indices[^3]);
            Assert.AreEqual(415, indices[^2]);
            Assert.AreEqual(417, indices[^1]);
        }

        [TestMethod()]
        public void GetLinesTest() {
            var mail = MakeSimpleMail();
            var lines = Program.GetLines(mail);

            Assert.AreEqual(13, lines.Count);
            Assert.AreEqual("From: a001 <a001@ah62.example.jp>\r\n", lines[0].ToUtf8String());
            Assert.AreEqual("Subject: test\r\n", lines[1].ToUtf8String());
            Assert.AreEqual("To: a002@ah62.example.jp\r\n", lines[2].ToUtf8String());

            Assert.AreEqual("Content-Language: en-US\r\n", lines[^3].ToUtf8String());
            Assert.AreEqual("\r\n", lines[^2].ToUtf8String());
            Assert.AreEqual("test", lines[^1].ToUtf8String());
        }

        [TestMethod()]
        public void IsNotUpdateTest() {
            Assert.IsFalse(Program.IsNotUpdate(true, true));
            Assert.IsFalse(Program.IsNotUpdate(true, false));
            Assert.IsFalse(Program.IsNotUpdate(false, true));
            Assert.IsTrue(Program.IsNotUpdate(false, false));
        }

        [TestMethod()]
        public void IsWspTest() {
            Assert.IsTrue(Program.IsWsp((byte)' '));
            Assert.IsTrue(Program.IsWsp((byte)'\t'));
            Assert.IsFalse(Program.IsWsp((byte)'\0'));
            Assert.IsFalse(Program.IsWsp((byte)'a'));
            Assert.IsFalse(Program.IsWsp((byte)'b'));
        }

        [TestMethod()]
        public void IsFirstWspTest() {
            Assert.IsTrue(Program.IsFirstWsp(new[] { (byte)' ', (byte)'a', (byte)'b' }));
            Assert.IsTrue(Program.IsFirstWsp(new[] { (byte)'\t', (byte)'a', (byte)'b' }));
            Assert.IsFalse(Program.IsFirstWsp(new[] { (byte)'\0', (byte)'a', (byte)'b' }));
            Assert.IsFalse(Program.IsFirstWsp(new[] { (byte)'a', (byte)'b', (byte)' ' }));
            Assert.IsFalse(Program.IsFirstWsp(new[] { (byte)'a', (byte)'b', (byte)'\t' }));
        }

        bool EqualSeqInSeq<T>(IEnumerable<IEnumerable<T>> list1, IEnumerable<IEnumerable<T>> list2) {
            if (list1.Count() != list2.Count())
                return false;

            return list1.Zip(list2).All(t => t.First.SequenceEqual(t.Second));
        }

        [TestMethod()]
        public void ReplaceDateLineTest() {
            var fMail = MakeFoldedMail();
            var lines = Program.GetLines(fMail);
            var newLines = Program.ReplaceDateLine(lines);
            Assert.IsFalse(EqualSeqInSeq(lines, newLines));

            var newMail = Program.ConcatBytes(newLines);
            CollectionAssert.AreNotEqual(fMail, newMail);
            Assert.AreNotEqual(GetDateLine(fMail), GetDateLine(newMail));
            Assert.AreEqual(GetMessageIdLine(fMail), GetMessageIdLine(newMail));
        }

        [TestMethod()]
        public void ReplaceMessageIdLineTest() {
            var fMail = MakeFoldedMail();
            var lines = Program.GetLines(fMail);
            var newLines = Program.ReplaceMessageIdLine(lines);
            CollectionAssert.AreNotEqual(lines, newLines);

            var newMail = Program.ConcatBytes(newLines);
            CollectionAssert.AreNotEqual(fMail, newMail);
            Assert.AreNotEqual(GetMessageIdLine(fMail), GetMessageIdLine(newMail));
            Assert.AreEqual(GetDateLine(fMail), GetDateLine(newMail));
        }

        [TestMethod()]
        public void ReplaceHeaderTest() {
            var mail = MakeSimpleMail();
            var dateLine = GetDateLine(mail);
            var midLine = GetMessageIdLine(mail);

            var replHeaderNoupdate = Program.ReplaceHeader(mail, false, false);
            CollectionAssert.AreEqual(mail, replHeaderNoupdate);

            var replHeader = Program.ReplaceHeader(mail, true, true);
            CollectionAssert.AreNotEqual(mail, replHeader);

            (string, string) Replace(byte[] header, bool updateDate, bool updateMessageId) {
                var rHeader = Program.ReplaceHeader(header, updateDate, updateMessageId);
                CollectionAssert.AreNotEqual(header, rHeader);
                return (GetDateLine(rHeader), GetMessageIdLine(rHeader));
            }

            var (rDateLine, rMidLine) = Replace(mail, true, true);;
            Assert.AreNotEqual(dateLine, rDateLine);
            Assert.AreNotEqual(midLine, rMidLine);

            var (rDateLine2, rMidLine2) = Replace(mail, true, false);
            Assert.AreNotEqual(dateLine, rDateLine2);
            Assert.AreEqual(midLine, rMidLine2);

            var (rDateLine3, rMidLine3) = Replace(mail, false, true);
            Assert.AreEqual(dateLine, rDateLine3);
            Assert.AreNotEqual(midLine, rMidLine3);

            var fMail = MakeFoldedMail();
            var (fDateLine, fMidLine) = Replace(fMail, true, true);
            Assert.AreEqual(1, fDateLine.Count(c => c == '\n'));
            Assert.AreEqual(1, fMidLine.Count(c => c == '\n'));
        }

        [TestMethod()]
        public void ConcatBytesTest() {
            var mail = MakeSimpleMail();
            var lines = Program.GetLines(mail);

            var newMail = Program.ConcatBytes(lines);
            CollectionAssert.AreEqual(mail, newMail);
        }

        [TestMethod()]
        public void CombineMailTest() {
            var mail = MakeSimpleMail();
            var (header, body) = Program.SplitMail(mail)!.Value;
            var newMail = Program.CombineMail(header, body);
            CollectionAssert.AreEqual(mail, newMail);
        }

        const byte CR = Program.CR;
        const byte LF = Program.LF;

        [TestMethod()]
        public void HasNextLfCrLfTest() {
            var zero = (byte)'\0';

            Assert.IsTrue(Program.HasNextLfCrLf(new[] { CR, LF, CR, LF}, 0));
            Assert.IsTrue(Program.HasNextLfCrLf(new[] { zero, CR, LF, CR, LF }, 1));

            Assert.IsFalse(Program.HasNextLfCrLf(new[] { CR, LF, CR, LF }, 1));
            Assert.IsFalse(Program.HasNextLfCrLf(new[] { CR, LF, CR, zero }, 0));
            Assert.IsFalse(Program.HasNextLfCrLf(new[] { CR, LF, CR, LF, zero }, 1));
        }

        [TestMethod()]
        public void FindEmptyLineTest() {
            var mail = MakeSimpleMail();
            Assert.AreEqual(414, Program.FindEmptyLine(mail));

            var invalidMail = MakeInvalidMail();
            Assert.AreEqual(-1, Program.FindEmptyLine(invalidMail));
        }

        [TestMethod()]
        public void SplitMail() {
            var mail = MakeSimpleMail();
            var headerBody = Program.SplitMail(mail);
            Assert.IsTrue(headerBody.HasValue);

            var (header, body) = headerBody!.Value;
            CollectionAssert.AreEqual(mail.Take(414).ToArray(), header);
            CollectionAssert.AreEqual(mail.Skip(414 + 4).ToArray(), body);

            var invalidMail = MakeInvalidMail();
            Assert.IsFalse(Program.SplitMail(invalidMail).HasValue);
        }

        [TestMethod()]
        public void ReplaceMailTest() {
            var mail = MakeSimpleMail();
            var replMailNoupdate = Program.ReplaceMail(mail, false, false);
            Assert.AreEqual(mail, replMailNoupdate);

            var replMail = Program.ReplaceMail(mail, true, true);
            Assert.AreNotEqual(mail, replMail);
            CollectionAssert.AreNotEqual(mail, replMail);
            CollectionAssert.AreEqual(mail[^100..^0], replMail![^100..^0]);

            var invalidMail = MakeInvalidMail();
            CollectionAssert.AreEqual(null, Program.ReplaceMail(invalidMail, true, true));
        }

        [TestMethod()]
        public void GetAndMapSettingsTest() {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, Program.MakeJsonSample());

            var settings = Program.MapSettings(Program.GetSettings(path));
            Assert.AreEqual("172.16.3.151", settings.SmtpHost);
            Assert.AreEqual(25, settings.SmtpPort);
            Assert.AreEqual("a001@ah62.example.jp", settings.FromAddress);
            CollectionAssert.AreEqual(new[] { "a001@ah62.example.jp", "a002@ah62.example.jp", "a003@ah62.example.jp" },
                settings.ToAddresses);
            CollectionAssert.AreEqual(new[] { "test1.eml", "test2.eml", "test3.eml" },
                settings.EmlFiles);
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
        public void SendMailTest() {
            var path = Path.GetTempFileName();
            var mail = MakeSimpleMail();
            File.WriteAllBytes(path, mail);

            var memStream = new MemoryStream();
            var sendLine = GetStdout(() => {
                Program.SendMail(memStream, path, false, false);
            });
            Assert.AreEqual($"send: {path}\r\n", sendLine);
            CollectionAssert.AreEqual(mail, memStream.ToArray());

            var memStream2 = new MemoryStream();
            Program.SendMail(memStream2, path, true, true);
            CollectionAssert.AreNotEqual(mail, memStream2.ToArray());
        }

        [TestMethod()]
        public void RecvLineTest() {
            var recvLine = GetStdout(() => {
                var streamReader = new StreamReader(new MemoryStream("250 OK\r\n".ToBytes()));
                Assert.AreEqual("250 OK", Program.RecvLine(streamReader));
            });
            Assert.AreEqual("recv: 250 OK\r\n", recvLine);

            var streamReader2 = new StreamReader(new MemoryStream());
            Assert.ThrowsException<Exception>(() => Program.RecvLine(streamReader2));

            var streamReader3 = new StreamReader(new MemoryStream("554 Transaction failed\r\n".ToBytes()));
            Assert.ThrowsException<Exception>(() => Program.RecvLine(streamReader3));
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
            Assert.AreEqual("EHLO localhost\r\n", memStream.ToArray().ToUtf8String());
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
                var noKey = json.Replace(key, $"X-{key}");
                Program.CheckSettings(Program.GetSettingsFromText(noKey));
            }

            Assert.ThrowsException<Exception>(() => checkNoKey("smtpHost"));
            Assert.ThrowsException<Exception>(() => checkNoKey("smtpPort"));
            Assert.ThrowsException<Exception>(() => checkNoKey("fromAddress"));
            Assert.ThrowsException<Exception>(() => checkNoKey("toAddresses"));
            Assert.ThrowsException<Exception>(() => checkNoKey("emlFiles"));

            try {
                checkNoKey("updateDate");
                checkNoKey("updateMessageId");
                checkNoKey("useParallel");
            } catch (Exception e) {
                Assert.Fail(e.Message);
            }
        }

        [TestMethod()]
        public void ProcJsonFileTest() {
            Assert.ThrowsException<Exception>(() => Program.ProcJsonFile("__test__"));
        }

        [TestMethod()]
        public void CheckJsonValueTest() {
            void Check(string jsonStr, JsonValueKind kind) {
                Program.CheckJsonValue(jsonStr.ToJson().RootElement, "test", kind);
            }

            void CheckError(string jsonStr, JsonValueKind kind, string expected) {
                try {
                    Check(jsonStr, kind);
                } catch (Exception e) {
                    Assert.AreEqual(expected, e.Message);
                }
            }

            var jsonStr = @"{""test"": ""172.16.3.151""}";
            var jsonNumber = @"{""test"": 172}";
            var jsonTrue = @"{""test"": true}";
            var jsonFalse = @"{""test"": false}";

            try {
                Check(jsonStr, JsonValueKind.String);
                Check(jsonNumber, JsonValueKind.Number);
                Check(jsonTrue, JsonValueKind.True);
                Check(jsonFalse, JsonValueKind.False);
            } catch (Exception e) {
                Assert.Fail(e.Message);
            }

            Assert.ThrowsException<Exception>(() => Check(jsonStr, JsonValueKind.Number));
            CheckError(jsonStr, JsonValueKind.True, "test: Invalid type: 172.16.3.151");

            Assert.ThrowsException<Exception>(() => Check(jsonNumber, JsonValueKind.String));
            CheckError(jsonNumber, JsonValueKind.False, "test: Invalid type: 172");

            Assert.ThrowsException<Exception>(() => Check(jsonTrue, JsonValueKind.String));
            CheckError(jsonTrue, JsonValueKind.Number, "test: Invalid type: True");

            Assert.ThrowsException<Exception>(() => Check(jsonFalse, JsonValueKind.String));
            CheckError(jsonFalse, JsonValueKind.Number, "test: Invalid type: False");
        }

        [TestMethod()]
        public void CheckJsonBoolValueTest() {
            void Check(string jsonStr) {
                Program.CheckJsonBoolValue(jsonStr.ToJson().RootElement, "test");
            }

            void CheckError(string jsonStr, string expected) {
                try {
                    Check(jsonStr);
                } catch (Exception e) {
                    Assert.AreEqual(expected, e.Message);
                }
            }

            try {
                var jsonTrue = @"{""test"": true}";
                Check(jsonTrue);

                var jsonFalse = @"{""test"": false}";
                Check(jsonFalse);
            } catch (Exception e) {
                Assert.Fail(e.Message);
            }

            var jsonStr = @"{""test"": ""172.16.3.151""}";
            CheckError(jsonStr, "test: Invalid type: 172.16.3.151");

            var jsonNumber = @"{""test"": 172}";
            CheckError(jsonNumber, "test: Invalid type: 172");
        }

        [TestMethod()]
        public void CheckJsonArrayValueTest() {
            void Check(string jsonStr, JsonValueKind kind) {
                Program.CheckJsonArrayValue(jsonStr.ToJson().RootElement, "test", kind);
            }

            void CheckError(string jsonStr, JsonValueKind kind, string expected) {
                try {
                    Check(jsonStr, kind);
                } catch (Exception e) {
                    Assert.AreEqual(expected, e.Message);
                }
            }

            try {
                var jsonArray = @"{""test"": [""172.16.3.151"", ""172.16.3.152"", ""172.16.3.153""]}";
                Check(jsonArray, JsonValueKind.String);
            } catch (Exception e) {
                Assert.Fail(e.Message);
            }

            var jsonStr = @"{""test"": ""172.16.3.151""}";
            CheckError(jsonStr, JsonValueKind.String, "test: Invalid type (array): 172.16.3.151");

            var jsonInvalidArray = @"{""test"": [""172.16.3.151"", ""172.16.3.152"", 172]}";
            CheckError(jsonInvalidArray, JsonValueKind.String, "test: Invalid type (element): 172");
        }
    }
}