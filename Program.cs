using System.Text;
using System.Net.Sockets;
using System.Diagnostics;
using System.Configuration;
using System.Data.SqlClient;
using System.Text.RegularExpressions;

namespace TakeProfitTest
{
    class Program
    {
        public enum CMD : int
        {
            StandBy = 0, GetNumber = 1, Exit = 2
        }
        public enum reqiestType : int
        {
            GetNumber = 0, GetNumberWithKey = 1, GetKey = 2, GetTask = 3, GetTaskSolution = 4
        }
        class taskInfo
        {
            public taskInfo(int n)
            { argument = n; command = CMD.StandBy; isResultReady = false; result = ""; expiredKey = ""; }

            public Task? task;
            public int taskNumber;
            public int argument;
            public string result;
            public CMD command;
            public bool isResultReady;
            public string expiredKey;
        }
        class keyKeeper
        {
            public string key="";
            public bool stopGuard = false;
            public bool pause = false;
        }
        static keyKeeper keyK = new keyKeeper();
        static void Main(string[] args)
        {
            var taskInfoList = new List<taskInfo>(); // Active task list
            const int MaxTasks = 200; // Maximum Tasks
            const int argMin = 1; // First argument
            const int argMax = 2018; // Last argument
            int numbersCount = 0; // The counter of numbers received from the server
            float answer = 0; // The final result
            var resultsArray = new List<int>(); //results array
            bool advancedQuiz=false; // Quize level. Simple = false, Advanced = true ).

            var isDBConnected = true; // In order to connect MS-SQL database, edit the dbName variable (below) and the connection string at the App.config file as well.
            var dbName = "TDB";
            var dbTableName = "takeProfit";
            SqlConnection conn=null;
            SqlCommand com = null;  
            if (isDBConnected)
            {
                conn = new SqlConnection(ConfigurationManager.ConnectionStrings["TestDb"].ConnectionString);
                try { conn.Open(); } catch (Exception e) { Console.WriteLine($"DB open error: {e.Message}"); isDBConnected = false; }
            }
            if (isDBConnected)
            {
                com = conn.CreateCommand();
                com.CommandText = $"IF object_id('[{dbName}].[dbo].[{dbTableName}]') IS NULL CREATE TABLE [{dbName}].[dbo].[{dbTableName}] ([arg] [int] NULL,[res] [int] NULL,[resAdv] [int] NULL)";
                try { com.ExecuteNonQuery(); }
                catch (Exception e) { Console.WriteLine($"DB Error: {e.Message}"); isDBConnected = false; }
            }
            var CheckRecord = (int a) =>
            {
                if(advancedQuiz)
                    com.CommandText = $"select resAdv from [{dbName}].[dbo].[{dbTableName}] where arg={a}";
                else
                    com.CommandText = $"select res from [{dbName}].[dbo].[{dbTableName}] where arg={a}";
                int? r;
                try { r = (int?)com.ExecuteScalar(); } catch { return null; }
                if (r is null || r == 0)
                    return null;
                return r;
            };
            var ResolveQuiz = () =>
            {
                Stopwatch Timer = new Stopwatch();
                Timer.Start();
                resultsArray.Clear();
                taskInfoList.Clear();
                var arg = argMin;
                numbersCount = 0;
                Task kg=null;

                while (true)
                {
                    int? lres;
                    if (advancedQuiz && (kg is null || kg.IsCompleted))
                    {
                        keyK.key = "not empty";
                        kg = Task.Factory.StartNew(() => KeyGuard(taskInfoList), TaskCreationOptions.LongRunning);
                    }
                    for (var i = 0; i < taskInfoList.Count; i++)
                    {
                        if (taskInfoList[i].isResultReady)
                        {
                            int res;
                            if (!int.TryParse(taskInfoList[i].result, out res))
                            {
                                taskInfoList[i].isResultReady = false;
                                taskInfoList[i].result = "";
                                taskInfoList[i].command = CMD.GetNumber;
                                continue;
                            }
                            numbersCount++;
                            resultsArray.Add(res);
                            if (isDBConnected)
                            {
                                if (advancedQuiz)
                                    com.CommandText = $"update [{dbName}].[dbo].[{dbTableName}] set [resAdv]={taskInfoList[i].result} where [arg]={taskInfoList[i].argument}";
                                else
                                    com.CommandText = $"insert into [{dbName}].[dbo].[{dbTableName}] ([arg],[res]) values ('{taskInfoList[i].argument}','{taskInfoList[i].result}')";


                                try { com.ExecuteNonQuery(); } catch (Exception e) { Console.WriteLine($"DB error!:{e.Message}"); }
                            }

                            Console.WriteLine($"(Task №={i:d3}){numbersCount:d4}) ({taskInfoList[i].argument:d4}=>{res:d7}) |{taskInfoList.Where(x => x.command == CMD.GetNumber).Count()}");
                            taskInfoList[i].result = "";
                            taskInfoList[i].isResultReady = false;

                            while (isDBConnected && (lres = CheckRecord(arg)) is not null)
                            {
                                arg++;
                                numbersCount++;
                                resultsArray.Add((int)lres);
                            }
                            if (arg <= argMax)
                            {
                                taskInfoList[i].taskNumber = i;
                                taskInfoList[i].argument = arg++;
                                taskInfoList[i].command = CMD.GetNumber;
                            }
                            else
                                taskInfoList[i].command = CMD.Exit;
                        }
                        if (taskInfoList[i].command == CMD.GetNumber && taskInfoList[i].task.IsCompleted)
                        {
                            taskInfoList[i].result = "";
                            taskInfoList[i].isResultReady = false;
                            taskInfoList[i].task = Task.Factory.StartNew(() => GetNumber(taskInfoList[i]), TaskCreationOptions.LongRunning);
                        }
                    }
                    while (isDBConnected && (lres = CheckRecord(arg)) is not null)
                    {
                        arg++;
                        numbersCount++;
                        resultsArray.Add((int)lres);
                    }
                    if ((arg <= argMax) && (taskInfoList.Count < MaxTasks))
                    {
                        var nti = new taskInfo(arg++); //, address, port, koi8r
                        taskInfoList.Add(nti);
                        nti.taskNumber = taskInfoList.Count - 1;
                        nti.command = CMD.GetNumber;
                        nti.task = Task.Factory.StartNew(() => GetNumber(nti), TaskCreationOptions.LongRunning);
                    }

                    if ((numbersCount > argMax - argMin))
                        break;
                }

                foreach (var ti in taskInfoList) ti.command = CMD.Exit;
                Timer.Stop();

                if (advancedQuiz)
                    keyK.stopGuard = true;

                resultsArray.Sort();
                answer = (float)(resultsArray[argMax / 2 - 1] + resultsArray[argMax / 2]) / 2;


                Console.WriteLine($"Elapsed time: {Timer.ElapsedMilliseconds:### ### ##0}ms ({Timer.Elapsed:hh\\:mm\\:ss\\.ffff}), Answer=({answer})");
                Console.WriteLine("Press Any Key...");
                Console.ReadKey();
                resultsArray.Clear();
                return answer;
            };

            Console.WriteLine(GetFeedBack("Greetings\n", reqiestType.GetTask));
            advancedQuiz = false;
            Console.WriteLine(GetFeedBack($"Check {ResolveQuiz()}\n", reqiestType.GetTaskSolution));
            advancedQuiz = true;
            Console.WriteLine(GetFeedBack($"Check_Advanced {ResolveQuiz()}\n", reqiestType.GetTaskSolution));
        }
        static void GetNumber(taskInfo IO_Block)
        {
            while (true)
            {
                try
                {
                    while (IO_Block.command == CMD.StandBy)
                        Thread.Sleep(500);

                    if (IO_Block.command == CMD.Exit)
                    {
                        IO_Block.command = CMD.StandBy;
                        return;
                    }
                    if (IO_Block.command == CMD.GetNumber)
                    {
                        IO_Block.isResultReady = false;
                        IO_Block.result = GetFeedBack(IO_Block.argument.ToString() + "\n", keyK.key.Length > 0 ? reqiestType.GetNumberWithKey : reqiestType.GetNumber, IO_Block);
                        IO_Block.command = CMD.StandBy;
                        IO_Block.isResultReady = true;
                    }
                }
                catch (Exception e)
                { 
                    Console.WriteLine($"\n\n\nGetNumber[t={IO_Block.taskNumber}]{e.Message}\n\n\n");
                    IO_Block.result = "";
                }
            }
        }
        static void KeyGuard(List<taskInfo> listTI)
        {
            Stopwatch Timer = new Stopwatch();
            var tSpans = new List<long>();
            while (true)
            {
                try
                {
                    if (keyK.stopGuard)
                        return;
                    if ((listTI.Where(x => x is not null && x.expiredKey == keyK.key).Count() > 0) || keyK.key.Length < 65 || Timer.ElapsedMilliseconds > 20000)
                    {
                        keyK.pause = true;
                        Timer.Stop();
                        var key = GetFeedBack("Register\n", reqiestType.GetKey);
                        lock (keyK)
                            keyK.key = key + "|";
                        keyK.pause = false;
                        if(Timer.ElapsedMilliseconds>0) tSpans.Add(Timer.ElapsedMilliseconds);
                        else tSpans.Add(20000);
                        Console.WriteLine($"\n\nKey life time: {Timer.ElapsedMilliseconds:### ### ##0}ms, Avarage key life time:{(tSpans.Sum() / tSpans.Count):### ### ##0}ms\n\n");
                        Timer.Reset();
                        Timer.Start();
                    }
                }
                catch (Exception e) { Console.WriteLine($"\n\n\nKeyGuard{e.Message}\n\n\n"); }
            }
        }
        static string GetFeedBack(string request, reqiestType rtype, taskInfo IO_Block=null)
        {
            const string address = "88.212.241.115";
            const int port = 2013;
            TcpClient? tcpc = null;
            NetworkStream? ns = null;
            var byteText = new List<byte>();
            string text = "";
            string storedKey = "";
            var tn = IO_Block is null ? "" : $"Task[{IO_Block.taskNumber}]";

            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var koi8r = Encoding.GetEncoding("koi8-r"); // code page = 20866

            int attempts;
            var SendRequest = () =>
            {
                byteText.Clear();
                if (rtype == reqiestType.GetNumberWithKey)
                {
                    while (keyK.key.Length < 65 || keyK.key == IO_Block.expiredKey || keyK.pause)
                        Thread.Sleep(300);
                    ns.Write(koi8r.GetBytes((storedKey=keyK.key) + request));
                }
                else
                    ns.Write(koi8r.GetBytes(request));
            };
            var ResetSocket = () =>
            {
                attempts = 0;
                if (!(tcpc is null))
                    tcpc.Close();
                while (true)
                {
                    while (rtype == reqiestType.GetNumberWithKey && keyK.pause)
                        Thread.Sleep(300);
                    try { tcpc = new TcpClient(address, port); break; }
                    catch (Exception e) { Console.WriteLine($"Socket open error(Request[{request}]Type[{rtype}]{tn}:{e.Message}"); Thread.Sleep(1000); }
                }
                ns = tcpc.GetStream();
                SendRequest();
            };

            ResetSocket();
            
            while (true)
            {
                attempts = 0;
                while (tcpc.Available <= 0)
                {
                    attempts++;
                    if (attempts > 40 || !tcpc.Connected)
                        ResetSocket();
                    Thread.Sleep(300);
                }
                byteText.Add((byte)ns.ReadByte());
                text = koi8r.GetString(byteText.ToArray(), 0, byteText.Count);
                //Console.WriteLine($"{tn}"+text);

                if (text.Contains("the Holy"))
                {
                    Console.WriteLine($"Request[{request}]Type[{rtype}]{tn}:'О╩© By the Holy Light! Unrecognized input line. Please check input string.'");
                    Thread.Sleep(200);
                    SendRequest();
                    continue;
                }
                if (text.Length < 20 && text.Contains("Rate limit"))
                {
                    Console.WriteLine($"Request[{request}]Type[{rtype}]{tn}:'Rate limit. Please wait some time then repeat.'");
                    Thread.Sleep(10000);
                    ResetSocket();
                    continue;
                }
                switch (rtype)
                {
                    case reqiestType.GetTask:
                    case reqiestType.GetTaskSolution:
                        if (text.Contains("(good luck)") || (request.Contains("Check_Advanced") && text.Contains("answer =)")))
                        {
                            tcpc.Close();
                            return (text);
                        }
                        break;
                    case reqiestType.GetKey:
                        if (text.Length >= 71)
                        {
                            tcpc.Close();
                            return text.Substring(3, 64);
                        }
                        break;
                    case reqiestType.GetNumber:
                    case reqiestType.GetNumberWithKey:
                        if (Regex.IsMatch(text, @"\D+\d+\D+"))
                        {
                            tcpc.Close();
                            return Regex.Replace(text, @"\D+(\d+)\D+", "$1");
                        }
                        if (text.Contains("Key has"))
                        {
                            IO_Block.expiredKey = storedKey;
                            Console.WriteLine($"{tn}:'Key has expired'");
                            ResetSocket();
                            continue;
                        }
                        break;
                }
            }
        }
    }
}