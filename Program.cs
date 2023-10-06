namespace GenerateArtifacts
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using LibGit2Sharp;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    class Program
    {
        /// <summary>
        /// Method for finding and applying Json Diff-Path on the Artifact
        /// </summary>
        /// <param name="path_qat">path to previous cloud</param>
        /// <param name="path_swe">path to previous cloud</param>
        /// <returns>File path of the new cloud artifact</returns>
        public static string Jsondiffpatch(string path_qat, string path_swe)
        {
            JObject jsonFile_qat = JObject.Parse(File.ReadAllText(path_qat));
            JObject jsonFile_swe = JObject.Parse(File.ReadAllText(path_swe));

            var patchDoc = Jsondiffpatch_.Diff(jsonFile_qat, jsonFile_swe);
            
            // Dictionary for tracking suggested changes
            Dictionary<string, List<string>> suggested_changes = new Dictionary<string, List<string>>();
            
            var keywords = new string[] { "SWEDEN", "Sweden", "sweden", "swe", "SWE" };
            var replacements = new string[] { "ISRAEL", "Israel", "israel", "isr", "ISR" };
            var count = patchDoc.Operations.Count();
            
            // Go through the operations in the patch document to modify the values for cloud names
            for (var i = 0; i < count; i ++)
            {
                var op = patchDoc.Operations[i];

                if (op.value != null && op.value.GetType() == typeof(JValue))
                {
                    var value = ((JValue)op.value).Value.ToString();
                    
                    // to find if the value is differing by a cloud name or any other difference in the files
                    bool found = false;

                    // Replacing with cloud names - could be given as input. 
                    foreach (var keyword in keywords)
                    {
                        if (value.Contains(keyword))
                        {
                            string newValue = value.Replace(keyword, replacements[keywords.ToList().IndexOf(keyword)]);
                            op.value = new JValue(newValue);
                            patchDoc.Replace(op.path, op.value);
                            found = true;
                            break;
                        }
                    }

                    // if not able to find the changes with cloud names mark as suggestions
                    if (!found)
                    {
                        // what if the operation is remove? -> do we want to keep such values? or maybe we can add them as suggestions
                        // current scenario removes the value (as per patch doc) and we will need to add an Add operation to get it back. (easy)

                        if (suggested_changes.ContainsKey(value))
                        {
                            suggested_changes[value].Add(op.path);
                        }
                        else
                        {
                            suggested_changes[value] = new List<string> { op.path };
                        }
                    }
                }
            }

            foreach (var token in suggested_changes)
            {
                // Marking these changes as suggestions and later we might have values for these to replace
                var str = "<Suggestion> " + token.Key;

                foreach (var path in token.Value)
                {
                    patchDoc.Replace(path, str);
                }
            }

            var patchDocJson = JsonConvert.SerializeObject(patchDoc, Formatting.Indented);
            // Console.WriteLine(patchDocJson);
            
            patchDoc.ApplyTo(jsonFile_qat);

            // Creating new json file
            var newJsonFile = JsonConvert.SerializeObject(jsonFile_qat, Formatting.Indented);
            // Console.WriteLine(newJson);

            var newPath = path_qat.Replace("QAT", "ISR_New");  // currently using ISR_New because ISR files already present - help in comparing our result
            // Console.WriteLine(newPath);
            
            File.WriteAllText(newPath, newJsonFile);
            return newPath;
        }

        /// <summary>
        /// Method for Adding New Entry to Artifacts containing information for multiple clouds
        /// </summary>
        /// <param name="filepath">file path to modify</param>
        /// <param name="cloud_swe">previous cloud name</param>
        /// <returns>Path of the edited file</returns>
        public static string NewEntryJson(string filepath, string cloud_swe)
        {
            JObject jsonFile = JObject.Parse(File.ReadAllText(filepath));

            var keywords = new string[] { "SWEDEN", "Sweden", "sweden", "swe", "SWE" };
            var replacements = new string[] { "ISRAEL", "Israel", "israel", "isr", "ISR" };

            int count = jsonFile.Descendants().OfType<JObject>().Count();

            // Keep track of the entries that have already been modified
            var modifiedEntries = new HashSet<JObject>();

            // Iterate over all entries in the JSON file
            for (int i=0; i<count; i++)
            {
                var entry = jsonFile.Descendants().OfType<JObject>().ElementAt(i);      
                
                // Find the parent array of the existing entry and add the new entry to the array
                var parent = entry.Parent;
                // Check if the entry has already been modified
                if (modifiedEntries.Contains(parent))
                {
                    continue;
                }

                // targeting the key 'Name' for each entry to identify the entry for previous cloud.
                var nameProperty = entry["Name"] != null? entry["Name"] : entry["name"];
                // var nameProperty = entry["Name"];

                if (nameProperty != null && nameProperty.ToString().Contains(cloud_swe))
                {
                    // Clone the existing entry and replace all occurrences of the cloud_swe substring with the newcloud substring
                    var newEntry = (JObject)entry.DeepClone();

                    foreach (var property in newEntry.Descendants().OfType<JProperty>())
                    {
                        // Console.WriteLine(property);
                        if (property.Value.Type == JTokenType.String)
                        {
                            var valueString = property.Value.ToString();
                            foreach (var keyword in keywords)
                            {
                                if (valueString.Contains(keyword))
                                {
                                    valueString = valueString.Replace(keyword, replacements[keywords.ToList().IndexOf(keyword)]);
                                    break;
                                }
                            }
                            property.Value = valueString;
                        }
                    }

                    Console.WriteLine(newEntry);

                    if (parent != null && parent.Type == JTokenType.Array)
                    {
                        ((JArray)parent).Add(newEntry);
                    }
                }
            }

            // Console.WriteLine(serviceResourceGroups.ToString());

            // Save the updated JSON file
            File.WriteAllText(filepath, jsonFile.ToString());

            return filepath;
        }

        /// <summary>
        /// Method to use Rest API to create a PR/adding comments .. 
        /// </summary>
        public static async void CreatePR()
        {
            try
            {
                var personalaccesstoken = "4ejqpp7w4gubthy5vjejbmcketl7spqyt7xnwpgcd3gbyzphx6ua";

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Accept.Add(
                        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(
                            Encoding.ASCII.GetBytes(
                                string.Format("{0}:{1}", "", personalaccesstoken))));

                    string getBranchid_url = "https://dev.azure.com/O365Exchange/O365%20Core/_apis/git/repositories/bf3ac165-37ff-4c93-b0e0-1b0498f8269c/refs?filter=heads/u/namagupta/IA_ISRAEL_Buildout&api-version=5.1";
                    
                    string israelBranch_id = "aeb1a39bd88bcb11bca4bd1445c5ef3fb03c8e35";     // this gets updated after every commit, fetch the new id everytime

                    // string pullReq_id = "2921642";

                    string repoUrl = "https://dev.azure.com/O365Exchange/O365%20Core/_apis/git/repositories/bf3ac165-37ff-4c93-b0e0-1b0498f8269c/pushes?api-version=5.1";

                    string commentURL = "https://dev.azure.com/O365Exchange/O365%20Core/_apis/git/repositories/bf3ac165-37ff-4c93-b0e0-1b0498f8269c/pullRequests/2921642/threads?api-version=7.0";

                    string change1 = "{ \"changeType\": \"edit\", \"item\": { \"path\": \"/ServiceModelResourcesSetup_ISR.json\" }, \"newContent\": { \"content\": \"" + File.ReadAllText(@"C:\Users\namagupta\source\repos\GenerateJson\GenerateJson\bin\Debug\ServiceModelFiles\ServiceModelResourcesSetup_ISR.json").Replace("\"", "\\\"") + "\", \"contentType\": \"rawtext\" } }, ";
                    
                    string fileEdited = "{ \"changeType\": \"edit\", \"item\": { \"path\": \"/ServiceModel_GraphContentSetup_GoLocal.json\" }, \"newContent\": { \"content\": \"" + File.ReadAllText(@"C:\Users\namagupta\source\repos\Privacy_Solution\sources\dev\Deployment\GriffinEV2\GraphContentFnApp\Setup\ServiceModel_GraphContentSetup_GoLocal.json").Replace("\"", "\\\"") + "\", \"contentType\": \"rawtext\" } }, ";

                    var requestBody = "{ \"refUpdates\": [{ \"name\": \"refs/heads/u/namagupta/IA_ISRAEL_Buildout\", \"oldObjectId\": \"b9d3a23246a67612f08b02a0f2dc9273a3dc5d6e\" }], \"commits\": [{ \"comment\": \"Artifacts for Israel\", \"changes\": [" + change1 + "] }] }";

                    var CommitRequstBody = "{ \"refUpdates\": [{ \"name\": \"refs/heads/u/namagupta/IA_ISRAEL_Buildout\", \"oldObjectId\": \"" + israelBranch_id + "\" }], \"commits\": [{ \"comment\": \"New changes pushed\", \"changes\": [" + fileEdited + "] }] }";

                    var CommentRequestBody = "{ \"comments\": [{ \"content\": \"```suggestion\nTrue\n```\", \"commentType\": 1 }], \"status\": 1, \"threadContext\": {\"filePath\": \"/DSRQueue.ISR.Parameters.json\", \"rightFileEnd\": { \"line\": 18, \"offset\": 35 }, \"rightFileStart\": {\"line\": 18, \"offset\": 23 } }, \"pullRequestThreadContext\": { \"changeTrackingId\": 1, \"iterationContext\": { \"firstComparingIteration\": 1, \"secondComparingIteration\": 2 } } }";

                    var content = new StringContent(CommitRequstBody, Encoding.UTF8, "application/json");

                    // using (HttpResponseMessage response = client.GetAsync(getBranchid_url).Result)
                    using (HttpResponseMessage response = client.PostAsync(repoUrl, content).Result)
                    {
                        response.EnsureSuccessStatusCode();
                        string responseBody = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(response.StatusCode);
                        Console.WriteLine(response.Content);
                        Console.WriteLine(responseBody);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// Method to clone repository
        /// </summary>
        public static void  CloneRepository()
        {
            string cloneUrl = "https://o365exchange.visualstudio.com/DefaultCollection/O365%20Core/_git/PrivacySolution";
            string localPath = @"C:\Users\namagupta\source\repos\Privacy_Solution_1";
            string personalAccessToken = "4ejqpp7w4gubthy5vjejbmcketl7spqyt7xnwpgcd3gbyzphx6ua";

            CloneOptions options = new CloneOptions();
            options.CredentialsProvider = (_url, _user, _cred) =>
                new UsernamePasswordCredentials
                {
                    Username = "PAT",
                    Password = personalAccessToken
                };

            Repository.Clone(cloneUrl, localPath, options);
        }
        
        /// <summary>
        /// Method to get all the files from locally cloned repository to modify and update Artifacts
        /// </summary>
        /// <param name="cloud1"></param>
        /// <param name="cloud2"></param>
        /// <returns>dictionary of service groups mapping to different files in each seervice group</returns>
        public static Dictionary<string, List<string>> GeneratePRs(string cloud1, string cloud2)
        {
            string repositoryPath = @"C:\Users\namagupta\source\repos\Privacy_Solution";
            string searchString = "ServiceModel";

            // dictionary to store the Artifacts for a single service group
            Dictionary<string, List<string>> fileMap = new Dictionary<string, List<string>>();

            using (var repo = new Repository(repositoryPath))
            {
                // Iterate over all the files in the repository
                foreach (var file in Directory.GetFiles(repositoryPath, "*", SearchOption.AllDirectories))
                {
                    // Check if the file name contains the search string and cloud1
                    if (Path.GetFileName(file).Contains(searchString) && Path.GetFileName(file).Contains(cloud1))
                    {
                        // Console.WriteLine(file);

                        // Replace cloud1 with cloud2 in the file name
                        string fileName = Path.GetFileName(file).Replace(cloud1, cloud2);

                        // Rolloutspec filenames
                        string RolloutSpecFileName = fileName.Replace("ServiceModel", "RolloutSpec");
                        string RolloutspecFilepath = Path.Combine(Path.GetDirectoryName(file), RolloutSpecFileName);

                        // path of other cloud Artifacts
                        string newPath = Path.Combine(Path.GetDirectoryName(file), fileName);

                        // Key to map the file path to the Servicegroup
                        string key = Path.GetDirectoryName(file);

                        // Check if a file with the modified name exists
                        if (File.Exists(newPath))
                        {
                            // Console.WriteLine(newPath);

                            if (fileMap.ContainsKey(key))
                            {
                                fileMap[key].Add(newPath);
                            }
                            else
                            {
                                fileMap[key] = new List<string> { newPath };
                            }

                            // Check if the file is a JSON file
                            if (Path.GetExtension(file).Equals(".json"))
                            {
                                // Parse the JSON file to get the location of the "Parameter.json" file
                                JObject json = JObject.Parse(File.ReadAllText(newPath));

                                foreach (var token in json.Descendants())
                                {
                                    // Parse the JSON file to get the location of the "Parameter.json" file
                                    if (token.Type == JTokenType.Property && ((JProperty)token).Value.Type == JTokenType.String)
                                    {
                                        // get parameter file path
                                        string paramPath = Path.GetDirectoryName(key);
                                        if (key.Contains("Setup"))
                                        {
                                            paramPath = Path.GetDirectoryName(paramPath);
                                        }

                                        string value = ((JProperty)token).Value.ToString();

                                        string paramfilefullpath = Path.Combine(paramPath, value);

                                        if (File.Exists(paramfilefullpath) && value.Contains("Parameters") && value.Contains(".json") && value.Contains(cloud2))
                                        {
                                            // Console.WriteLine(value);

                                            // Map the file path to the directory path
                                            if (fileMap.ContainsKey(key))
                                            {
                                                fileMap[key].Add(paramfilefullpath);
                                            }
                                            else
                                            {
                                                fileMap[key] = new List<string> { paramfilefullpath };
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (File.Exists(RolloutspecFilepath))
                        {
                            // Console.WriteLine(RolloutspecFilepath);

                            if (fileMap.ContainsKey(key))
                            {
                                fileMap[key].Add(RolloutspecFilepath);
                            }
                            else
                            {
                                fileMap[key] = new List<string> { RolloutspecFilepath };
                            }


                            // add the [golocal] type of servicemodel files specified in this rolloutspec

                            // Parse the RolloutSpec JSON file to get the location of the "ServiceModel_Golocal" file
                            JObject rolloutSpecJson = JObject.Parse(File.ReadAllText(RolloutspecFilepath));
                            foreach (var token in rolloutSpecJson.Descendants())
                            {
                                if (token.Type == JTokenType.Property && ((JProperty)token).Value.Type == JTokenType.String)
                                {
                                    var value = ((JProperty)token).Value.ToString();

                                     var filepath = Path.Combine(Path.GetDirectoryName(file), value);

                                    if (value.Contains(searchString) && File.Exists(filepath))
                                    {
                                        // Console.WriteLine(filepath);
                                        
                                        // getting rolloutspec_golocal type of files    -> these files have diffrent implemntation of new clouds other than ServiceModel files 
                                        var rolloutspec_golocal = value.Replace(searchString, "RolloutSpec");
                                        if (File.Exists(rolloutspec_golocal))
                                        {
                                            fileMap[key].Add(Path.Combine(Path.GetDirectoryName(rolloutspec_golocal), rolloutspec_golocal));
                                        }
                                        
                                        // adding the serviceModel_golocal files to the servicegroup
                                        fileMap[key].Add(filepath);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return fileMap;
        }

        /// <summary>
        /// Main Method to trigger the flow
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            CreatePR();
            // CloneRepository();

            var cloud_qat = "QAT";
            var cloud_swe = "SWE";

            // Get mapping of different servicegrouproot folders to the Json Artifacts
            // Dictionary<string, List<string>> fileMap = GeneratePRs(cloud_qat, cloud_swe);

            // foreach (string path in fileMap.Keys)
            // {
            //     Console.WriteLine(path);
                
            //     foreach (var filePath in fileMap[path])
            //     {
            //         if (filePath.Contains(cloud_swe))
            //         {
            //             // newPath is defined here for the second file
            //             string path_qat = filePath.Replace(cloud_swe, cloud_qat);

            //             Console.WriteLine(filePath);
                        
            //             // generate new Json with these 2 files -> filePath and newPath
            //             if (File.Exists(path_qat) && File.Exists(filePath))
            //             {
            //                 Console.WriteLine(path_qat);

            //                 // create new file
            //                 string path_isr = Jsondiffpatch(path_qat, filePath);
            //             }
            //         }
            //         else  // This case when Artifacts of type ServiceModel_golocal
            //         {
            //             string path_isr = NewEntryJson(filePath, cloud_swe);
            //         }
            //     }
            //     // this is end of one service group : Raise a new PR here
            //     // CreatePR();

            // }

            /*string path_qat = "C:\\Users\\namagupta\\source\\repos\\Privacy_Solution\\sources\\dev\\Deployment\\GriffinEV2\\Parameters\\PrivacyARMServiceSetup\\PrivacyARMServiceSetup.Parameters.QAT.json";
            string path_swe = "C:\\Users\\namagupta\\source\\repos\\Privacy_Solution\\sources\\dev\\Deployment\\GriffinEV2\\Parameters\\PrivacyARMServiceSetup\\PrivacyARMServiceSetup.Parameters.SWE.json";
            Jsondiffpatch(path_qat, path_swe);*/

            // string path_file = "C:\\Users\\namagupta\\source\\repos\\Privacy_Solution\\sources\\dev\\Deployment\\GriffinEV2\\GraphContentFnApp\\Setup\\ServiceModel_GraphContentSetup_GoLocal.json";
            // string newpath = NewEntryJson(path_file, cloud_swe);
            Console.ReadLine();
        }
    }
}