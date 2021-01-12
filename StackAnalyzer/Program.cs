using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using NLog;
using RestSharp;
using StackAnalyzer.Common;

namespace StackAnalyzer
{
    class Program
    {
        private static readonly string FilterQuestions = ConfigurationManager.AppSettings["FilterQuestions"];
        private static readonly string MapsAPIKey = ConfigurationManager.AppSettings["GoogleGeoAPIKey"];
		private static ILogger _logger = LogManager.GetCurrentClassLogger();
        private static string LocationsFile = "sp_users_locations.json";
        private static string QueryOverLimitFile = "query_over_limit.json";
        private static int MapsAPISleepInterval = 100;


        static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Ssl3;

            //LoadUsers("sharepoint");

            //LoadAnswers("sharepoint");

            //LoadQuestions("sharepoint");

            //LoadTags("sharepoint");

            HandleSOSpecialCase();

            //PopulateUsersLocation();
        }

        private static void HandleSOSpecialCase()
        {
            if (File.Exists("so_questions.json"))
            {
                //File.Delete("so_questions.json");
            }

            //LoadSOQuestions("stackoverflow", new Parameter { Name = "tagged", Value = "microsoft-graph-api" });
            //LoadSOQuestions("stackoverflow", new Parameter { Name = "tagged", Value = "microsoft-teams" });

            var questions = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText("so_questions.json"));
            var distinctQuestions = questions.GroupBy(q => q["question_id"]).Select(g => g.First()).ToList();
            File.WriteAllText("so_questions.json", JsonConvert.SerializeObject(distinctQuestions));

            var answers = new List<dynamic>();
            foreach (var question in distinctQuestions)
            {
                var questionAnswers = question["answers"];
                if(questionAnswers == null)
                {
                    continue;
                }
                answers.AddRange(questionAnswers);
            }

            File.WriteAllText("so_answers.json", JsonConvert.SerializeObject(answers));
        }

        private static void PopulateUsersLocation(int skip = 0)
        {
            var queryOverLimit = new List<dynamic>();

            try
            {
                var usersData = File.ReadAllText("sp_users.json");
                var users = JsonConvert.DeserializeObject<List<dynamic>>(usersData);

                var usersWithLocation = users.Where(u => u["location"] != null).Skip(skip).ToList();

                int i = skip;
                foreach (dynamic user in usersWithLocation)
                {
                    i++;
                    var locationString = user["location"].Value;
                    var userId = user["user_id"].Value;
                    //_logger.Info($"Processing user {i++} out of {usersWithLocation.Count}, user id: {userId}, location: {locationString}");

                    var locationData = LoadUsersLocationData();
                    if (locationData.Any(d => d["location"] == locationString))
                    {
                        var existingLocation = locationData.First(d => d["location"] == locationString);
                        if (locationData.Any(d => d["user_id"] == userId))
                        {
                            //_logger.Info($"Skipping user {userId} - already in file");
                            continue;
                        }
                        locationData.Add(new
                        {
                            user_id = userId,
                            location = existingLocation["location"],
                            country = existingLocation["country"]
                        });

                        File.WriteAllText(LocationsFile, JsonConvert.SerializeObject(locationData));

                        //_logger.Info($"Found exact location, userid: {existingLocation["user_id"]}, locaton: {existingLocation["location"]}, country: {existingLocation["country"]}");

                        continue;
                    }

                    var client = new RestClient("https://maps.googleapis.com/maps/api/geocode/json");

                    //var proxyUri = "http://150.95.190.102";
                    //client.Proxy = new WebProxy(new Uri(proxyUri), false);

                    var geoRequest = new RestRequest
                    {
                        Method = Method.GET,
                        Timeout = 100 * 1000
                    };
                    geoRequest.AddQueryParameter("address", locationString);
                    geoRequest.AddQueryParameter("key", MapsAPIKey);

					var geoResult = client.Execute<dynamic>(geoRequest).Data;

                    if (geoResult["status"] != "OK")
                    {
                        if (geoResult["status"] == "OVER_QUERY_LIMIT")
                        {
                            AddQueryLimitError(user);
                        }
                        if (geoResult["status"] == "ZERO_RESULTS")
                        {
                            locationData.Add(new
                            {
                                user_id = userId,
                                location = locationString,
                                country = "NOT_FOUND"
                            });

                            File.WriteAllText(LocationsFile, JsonConvert.SerializeObject(locationData));
                            Thread.Sleep(MapsAPISleepInterval);
                        }
                        _logger.Error($"Unable to parse the result. Result code: {geoResult["status"]}");
                        continue;
                    }

                    _logger.Info($"{i} out of {usersWithLocation.Count}");
                    AddUserLocation(locationData, geoResult, user);

                    File.WriteAllText(LocationsFile, JsonConvert.SerializeObject(locationData));
                    Thread.Sleep(MapsAPISleepInterval);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private static void AddUserLocation(List<dynamic> userLocationsData, dynamic geoResult, dynamic user)
        {
            dynamic addressComponents = geoResult["results"][0]["address_components"];

            foreach (dynamic addressComponent in addressComponents)
            {
                dynamic types = addressComponent["types"];

                foreach (dynamic type in types)
                {
                    if (type == "country")
                    {
                        _logger.Info($"Found country: {addressComponent["long_name"]}");
                        userLocationsData.Add(new
                        {
                            user_id = user["user_id"].Value,
                            location = user["location"].Value,
                            country = addressComponent["long_name"]
                        });
                        return;
                    }
                }
            }
            userLocationsData.Add(new
            {
                user_id = user["user_id"].Value,
                location = user["location"].Value,
                country = "NOT_FOUND"
            });
            _logger.Error($"Unable to find country location for user: {user["user_id"].Value}");
        }

        private static List<dynamic> LoadUsersLocationData()
        {
            if (File.Exists(LocationsFile))
            {
                return JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(LocationsFile));
            }

            return new List<dynamic>();
        }

        private static void AddQueryLimitError(dynamic user)
        {
            List<dynamic> queryOverLimit;
            if (File.Exists(QueryOverLimitFile))
            {
                queryOverLimit = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(QueryOverLimitFile));
            }
            else
            {
                queryOverLimit = new List<dynamic>();
            }

            queryOverLimit.Add(new
            {
                user_id = user["user_id"].Value,
                location = user["location"].Value
            });

            File.WriteAllText(QueryOverLimitFile, JsonConvert.SerializeObject(queryOverLimit));
        }

        private static void LoadTags(string source, params Parameter[] additionalParams)
        {
            var tagsParameters = new List<Parameter>
            {
                new Parameter
                {
                    Name = "filter",
                    Value = FilterQuestions
                }
            };

            foreach (var param in additionalParams)
            {
                tagsParameters.Add(param);
            }

            var tagsLoader = new StackSaver("tags", source, tagsParameters);
            tagsLoader.LoadAndSaveData("sp_tags.json");
        }

        private static void LoadQuestions(string source, params Parameter[] additionalParams)
        {
            var questionsParams = new List<Parameter>
            {
                new Parameter
                {
                    Name = "order",
                    Value = "desc"
                },
                new Parameter
                {
                    Name = "sort",
                    Value = "creation"
                },
                new Parameter
                {
                    Name = "filter",
                    Value = FilterQuestions
                }
            };

            foreach (var param in additionalParams)
            {
                questionsParams.Add(param);
            }

            var questionsLoader = new StackSaver("questions", source, questionsParams);
            questionsLoader.LoadAndSaveData("sp_questions.json");
        }

        private static void LoadSOQuestions(string source, params Parameter[] additionalParams)
        {
            var questionsParams = new List<Parameter>
            {
                new Parameter
                {
                    Name = "order",
                    Value = "desc"
                },
                new Parameter
                {
                    Name = "sort",
                    Value = "creation"
                },
                new Parameter
                {
                    Name = "filter",
                    Value = FilterQuestions
                }
            };

            foreach (var param in additionalParams)
            {
                questionsParams.Add(param);
            }

            var questionsLoader = new StackSaver("questions", source, questionsParams);
            questionsLoader.LoadAndSaveData("so_questions.json", 1, true);
        }

        private static void LoadAnswers(string source, params Parameter[] additionalParams)
        {
            var questionsParams = new List<Parameter>
            {
                new Parameter
                {
                    Name = "order",
                    Value = "desc"
                },
                new Parameter
                {
                    Name = "sort",
                    Value = "creation"
                },
                new Parameter
                {
                    Name = "filter",
                    Value = FilterQuestions
                }
            };

            foreach (var param in additionalParams)
            {
                questionsParams.Add(param);
            }

            var questionsLoader = new StackSaver("answers", source, questionsParams);
            questionsLoader.LoadAndSaveData("sp_answers.json");
        }

        private static void LoadUsers(string source, params Parameter[] additionalParams)
        {
            var usersParams = new List<Parameter>
            {
                new Parameter
                {
                    Name = "order",
                    Value = "desc"
                },
                new Parameter
                {
                    Name = "sort",
                    Value = "creation"
                },
                new Parameter
                {
                    Name = "filter",
                    Value = FilterQuestions
                }
            };

            foreach (var param in additionalParams)
            {
                usersParams.Add(param);
            }

            var questionsLoader = new StackSaver("users", source, usersParams);
            questionsLoader.LoadAndSaveData("sp_users.json");
        }
    }
}
