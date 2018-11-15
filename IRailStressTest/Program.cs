using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;


namespace IRailStressTest
{
    class Program
    {
        private readonly static Random r = new Random();

        static void Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | 
                SecurityProtocolType.Tls11 | 
                SecurityProtocolType.Tls;
            MainAsync(args).Wait();
            // or, if you want to avoid exceptions being wrapped into AggregateException:
            //  MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            var testSets =
                new List<string>();
            for (int i = 0; i <= 17; i++)
            {
                testSets.Add(i.ToString());
            }


            ThreadPool.GetMaxThreads(out var workerThreads, out var completionPortThreads);
            Console.WriteLine($"{workerThreads},{completionPortThreads}");
            ThreadPool.SetMaxThreads(workerThreads, workerThreads / 2);
            System.Net.ServicePointManager.DefaultConnectionLimit = 100;

            // We want a throughput of 200/s
            // This should run ~1minute
            int maxNumberOfTests = 100;
            // After 'timeout' seconds, we'll stop testing
            // 
            int target = 400; // queries per second
            target = target / 100; // Divided by number of started processesss
            int spread = maxNumberOfTests / target; // around 250 sec
            int timeOut = spread * 2;


            var index = "";
            if (args.Length > 0)
            {
                index = "-" + args[0];
            }

            var resultDestination = $"results-{DateTime.Now:yyyy-MM-dd_HHmmss}{index}.csv";

            EnableLogging();
            Log.Information("IRail Stresstest");
            Log.Information("I'm sorry, Bert. Ben made me do this.");
            ThreadPool.GetAvailableThreads(out int workerThreadsAv, out int totalThreads);
            ThreadPool.GetMaxThreads(out int workerThreadsMax, out int totalThreadsMax);
            Log.Information($"Threadpool has {workerThreadsAv}/{workerThreadsMax} worker threads availableF");
            Log.Information($"Threadpool has {totalThreads}/{totalThreadsMax} total threads availableF");

            Log.Information("Generating queries...");
            var queries = GenerateQueries(testSets, maxNumberOfTests);
            Log.Information("Unleashing the beast...");
            var results = await RunQueries(queries, timeOut, spread);
            Log.Information("Writing results...");
            results.Sort();
            File.WriteAllLines(resultDestination, new List<string> {CsvHeader});
            File.AppendAllLines(resultDestination, results);

            Log.Information($"Done. You can get your results by analyzing {resultDestination}");
        }


        private static List<string> GenerateQueries
            (IEnumerable<string> testSets, int maxNumberOfTests)
        {
            var queryList = new List<string>();

            var minuteDiff = 0;

            while (queryList.Count < maxNumberOfTests)
            {
                foreach (var testSet in testSets)
                {
                    var testSetpath = $"queries/round{testSet}.jsonstream";
                    var cases = File.ReadAllLines(testSetpath);
                    foreach (var @case in cases)
                    {
                        if (queryList.Count >= maxNumberOfTests)
                        {
                            break;
                        }

                        var jsonCase = JObject.Parse(@case);
                        var result = CreateQueryFor(jsonCase, minuteDiff);
                        queryList.Add(result);
                    }
                }

                minuteDiff++;
            }

            return queryList;
        }


        public static string CreateQueryFor(JObject test, int timeDrift = 0)
        {
            var dep = test.GetValue("departureStop").ToString();
            var arr = test.GetValue("arrivalStop").ToString();
            var depTime = DateTime.Parse(test.GetValue("departureTime").ToString());


            depTime.AddMinutes(timeDrift);

            return $"https://api.irail.be/connections/?to={arr}&" +
                   $"from={dep}&" +
                   $"date={depTime:ddMMyy}&time={depTime:HHmm}&" +
                   $"timeSel=depart";
        }

        private static async Task<List<string>> RunQueries(List<string> queries, int timeOut, int spread)
        {
            var deadline = DateTime.Now.AddSeconds(timeOut);

            var results = new ConcurrentBag<string>();
            var tasks = new Task[queries.Count];

            for (var i = 0; i < tasks.Length; i++)
            {
                var query = queries[i];
                var name = i.ToString();
                tasks[i] = Task.Run(async () =>
                {
                    var result = await RunTestCase(query, deadline, spread, i);
                    results.Add(result);
                });
            }

            Task.WaitAll(tasks);

            var resultStrings = new List<string>(results);

            return resultStrings;
        }

        private static ThreadLocal<HttpClient> LocalHttpClient = new ThreadLocal<HttpClient>(() =>
        {
            HttpClientHandler hch = new HttpClientHandler();
            hch.Proxy = null;
            hch.UseProxy = false;
            hch.UseCookies = false;
            hch.AllowAutoRedirect = false;
            hch.PreAuthenticate = false;
            hch.CheckCertificateRevocationList = false;
            hch.MaxConnectionsPerServer = 1000;
            var client = new HttpClient(hch);

            client.DefaultRequestHeaders.Add("user-agent",
                "IRailStressTest-Anyways/0.0.1 (anyways.eu; pieter@anyways.eu)");
            client.DefaultRequestHeaders.Add("accept", "application/json");
            client.Timeout = TimeSpan.FromMilliseconds(10000000);

            return client;
        });

        /// <summary>
        /// Runs the given testcase. Returns 
        /// </summary>
        /// <param name="test">The JSON-object containing the test data</param>
        /// <returns>A comma-seperated string, containing {query start time},{time needed},{response size|FAILED},{query}</returns>
        public static async Task<string> RunTestCase(string queryString, DateTime deadline, int spread, int name)
        {
            var client = LocalHttpClient.Value;

            var wait = r.Next(0, spread * 1000);
            await Task.Delay(wait);

            var start = DateTime.Now;
            if (start > deadline)
            {
                return $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},0,TIMEOUT - NOT STARTED,{queryString}";
            }

            HttpResponseMessage response = null;
            try
            {
                response = await client.GetAsync(new Uri(queryString));
            }
            catch (Exception e)
            {
                var errMsg = e.Message;
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    errMsg += ": " + e.Message;
                }

                return $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},0,ERROR {errMsg},{queryString}";
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                var endFailed = DateTime.Now;

                var timeNeededFailed = (int) (endFailed - start).TotalMilliseconds;

                return
                    $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},{timeNeededFailed},FAILED HTTP:{response?.StatusCode},{queryString}";
            }

            var data = await response.Content.ReadAsStringAsync();

            var end = DateTime.Now;

            var timeNeeded = (int) (end - start).TotalMilliseconds;

            //        Log.Information(
            //              $"{name}:{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},{end:yyyy-MM-dd},{end:HH:mm:ss:ffff},{timeNeeded},{data.Length},{queryString}");
            return $"{start:yyyy-MM-dd},{start:HH:mm:ss:ffff},{timeNeeded},{data.Length},{queryString}";
        }


        public static string CsvHeader =
            "QueryDate,QueryHour,NeededTimeMilliSec,ResponseLength,Query";

        private static void EnableLogging()
        {
            // initialize serilog.
            var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logFile = Path.Combine("logs", $"log-{date}.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File(new JsonFormatter(), logFile)
                .WriteTo.Console()
                .CreateLogger();
        }
    }
}