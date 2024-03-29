﻿using AngleSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ikkdm_fch
{
    class Program
    {
        private static readonly Regex ScriptRegex = new Regex("<IMG SRC='(?<url>.*?)'>");
        private static string baseLocation = "./download";
        private static readonly HttpClient httpClient = new HttpClient();

        private static readonly IBrowsingContext context = BrowsingContext.New(Configuration.Default.WithOnly(new Utf8MetaHandler()));

        private static int count;
        private static int failCount;
        private static int connection;

        private static readonly HashSet<string> includes = new HashSet<string>();
        private static readonly HashSet<string> excludes = new HashSet<string>();

        static void Main(string[] args)
        {
            Console.Write("ikkdm的移动版链接 >>> ");
            var homeUrl = Console.ReadLine();
            Console.Write("限制同时下载的话数(默认10，-1不限制): ");
            if (!int.TryParse(Console.ReadLine(), out var maxConnection))
            {
                maxConnection = 10;
            }

            // Includes
            Console.Write("包括（设置包含项，空为全部）: ");
            Console.ReadLine().Split(",").Where(i => i!=string.Empty).ToList().ForEach(i => includes.Add(i));

            // Excludes
            Console.Write("不包括（设置剔除项）：");
            Console.ReadLine().Split(",").Where(e => e != string.Empty).ToList().ForEach(e => excludes.Add(e));

            var document = context.OpenAsync(res => res.Content(httpClient.GetByteArrayAsync(homeUrl).Result.GbkByteArrayToUtf8String())).Result;

            var title = document.QuerySelector("#comicName").TextContent;

            var episodes = document
                .QuerySelector("#list")
                .QuerySelectorAll("li > a[href]")
                .Where(e => (includes.Count == 0 || Contains(includes, e.TextContent.Split(' ')[1]) && !Contains(excludes, e.TextContent.Split(' ')[1])))
                .Select(e => new Episode { Link = e.GetAttribute("href"), Title = e.TextContent })
                .ToList();

            if (maxConnection <= 0 || maxConnection > episodes.Count)
            {
                maxConnection = episodes.Count;
            }
            Console.WriteLine("最大请求数: " + maxConnection.ToString());

            if (!Directory.Exists(baseLocation))
            {
                Directory.CreateDirectory(baseLocation);
            }

            baseLocation = Path.Join(baseLocation, title);

            if (!Directory.Exists(baseLocation))
            {
                Directory.CreateDirectory(baseLocation);
            }

            Console.WriteLine("-------- 一共" + episodes.Count + "话 --------");
            Console.WriteLine("-------- 开始下载 --------");
            for (int i = 0; i < episodes.Count;)
            {
                if (connection < maxConnection)
                {
                    Interlocked.Increment(ref connection);
                    _ = FchEpisode(episodes[i++]);
                }
                else
                {
                    Task.Delay(1_000).Wait();
                }
            }
            while (count < episodes.Count)
            {
                Task.Delay(1_000).Wait();
            }
            Console.WriteLine("-------- 下载完成 --------");
        }

        private static async Task FchEpisode(Episode episode)
        {
            var url = "http://m.ikkdm.com" + episode.Link;

            var content = await httpClient.GetByteArrayAsync(url);
            var doc = await context.OpenAsync(res => res.Content(content.GbkByteArrayToUtf8String()));
            var box = doc.QuerySelector("div.classBox.autoHeight");

            var info = box.QuerySelectorAll("div.bottom ul.subNav li").Skip(1).Take(1).First().TextContent;
            var episodeCount = int.Parse(info.Split('/')[1]);

            // 创建当前话的下载目录
            var location = Path.Join(baseLocation, episode.Title);
            if (!Directory.Exists(location))
            {
                Directory.CreateDirectory(location);
            }

            var baseUrl = url.Substring(0, url.LastIndexOf('/') + 1);
            Console.WriteLine(episode.Title + " " + episodeCount + "图");
            await Enumerable.Range(0, episodeCount)
                   .Select(i => FchImage(episode.Title, baseUrl + (i + 1).ToString() + ".htm"))
                   .ToArray()
                   .WhenAll();

            Interlocked.Increment(ref count);
            Interlocked.Decrement(ref connection);
            Console.WriteLine("{{ " + episode.Title + " }}下载完成");
            Console.WriteLine("已保存" + count.ToString() + "话, 有" + failCount + "张图下载遇到错误");
        }

        private static async Task FchImage(string title, string url)
        {
            var content = await httpClient.GetByteArrayAsync(url);
            var doc = await context.OpenAsync(res => res.Content(content.GbkByteArrayToUtf8String()));
            var box = doc.QuerySelector("div.classBox.autoHeight");

            var script = box.QuerySelector("script[language=javascript]").TextContent;
            var matchedUrl = ScriptRegex.Match(script).Groups["url"].Value.Replace("\"+m2007+\"", "http://m8.1whour.com/");

            var i1 = url.LastIndexOf('/');
            var i2 = url.LastIndexOf('.');
            var imgName = url.Substring(i1 + 1, i2 - i1 - 1);
            try
            {
                await SaveImage(matchedUrl, title, imgName);
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref failCount);
                Console.Error.WriteLine("从" + matchedUrl + "下载遇到错误" + Environment.NewLine + e.StackTrace);
            }
        }

        private static async Task SaveImage(string url, string title, string imgName)
        {
            var stream = await httpClient.GetByteArrayAsync(url);
            var ext = Path.GetFileName(url);
            var path = Path.Join(baseLocation, title, imgName + "_" + ext);
            using (var fs = File.Create(path, stream.Length, FileOptions.Asynchronous))
            {
                await fs.WriteAsync(stream, 0, stream.Length);
            }
        }

        private static bool Contains(HashSet<string> collection, string order)
        {
            foreach (var it in collection)
            {
                if(order.StartsWith(it))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
