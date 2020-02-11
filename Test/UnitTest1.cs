using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using NLog.Config;
using NLog.Targets;
using RestProxy;

namespace Test
{
    [TestClass]
    public class UnitTest1 {
        private RestProxyFactory _factory;

        [TestInitialize]
        public void Init() {
            var conf = new LoggingConfiguration();
            var consoleTarget = new ColoredConsoleTarget("console") {
                Layout = @"${time} ${level} [${threadId}] ${logger} - ${message} ${exception}"
            };
            conf.AddTarget(consoleTarget);
            conf.AddRuleForAllLevels(consoleTarget);
            LogManager.Configuration = conf;

            var httpClient = new HttpClient(new HttpClientHandler {
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = false
            });
            _factory = new RestProxyFactory(httpClient) {
                Serializer = JsonSerializer.Create(new JsonSerializerSettings {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                })
            };
        }

        [TestMethod]
        public void TestMethod1() {

            //var client = RestProxy<IJsonPlaceholder>.Create(httpClient, serializer);
            var client = _factory.Create<IJsonPlaceholder>();

            var posts = client.ListPosts().GetAwaiter().GetResult();
            //var post = client.ListPostsSync();

            Assert.IsTrue(posts.Count > 0);
            Console.WriteLine(posts.First());
        }
    }

    [RestContract(Namespace = "https://jsonplaceholder.typicode.com")]
    interface IJsonPlaceholder {
        [Get("/posts/{id}")]
        Task<Post> Get([UrlParam] int id);

        [Get("/posts")]
        Task<List<Post>> ListPosts();

        [Get("/posts")]
        List<Post> ListPostsSync();
    }

    class Post {
        public int UserId { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }

        public override string ToString() => $"Post[userId={UserId}, id={Id}, title={Title}, body={Body}]";
    }
}
