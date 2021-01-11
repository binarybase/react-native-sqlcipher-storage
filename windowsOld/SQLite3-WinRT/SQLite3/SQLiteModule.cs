using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using ReactNative.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Storage;
using SQLite3;
using ReactNative.Modules.Core;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace ReactNative.Modules.SQLite
{
    public class SQLiteModule : ReactContextNativeModuleBase, ILifecycleEventListener
    {
        static string version;
        static Dictionary<string, Database> databases = new Dictionary<string, Database>();
        static Dictionary<string, string> databaseKeys = new Dictionary<string, string>();


        public SQLiteModule(ReactContext reactContext) : base(reactContext)
        {

        }

        public override string Name
        {
            get
            {
                return "SQLite";
            }

        }

        [ReactMethod]
        public async void open(
            JObject config,
            ICallback doneCallback,
            ICallback errorCallback
            )
        {
            try
            {
                string dbname = config.Value<string>("name") ?? "";
                string opendbname = ApplicationData.Current.LocalFolder.Path + "\\" + dbname;
                string key = config.Value<string>("key");
                //Database db = await (key != null ? Database.OpenAsyncWithKey(opendbname, key) : Database.OpenAsync(opendbname));
                Database db = await Database.OpenAsyncWithKey(opendbname, key);
                if (version == null)
                {
                    JObject result = JObject.Parse(await db.OneAsyncVector("SELECT sqlite_version() || ' (' || sqlite_source_id() || ')' as version", new List<string>()));
                    version = result.Value<string>("version");
                }
                databases[dbname] = db;
                databaseKeys[dbname] = key;
                doneCallback.Invoke();
                //Debug.WriteLine("Opened database");

            }
            catch (Exception e)
            {
                errorCallback.Invoke(e.Message);
                Debug.WriteLine("Open database failed " + e.ToString());
            }


        }

        [ReactMethod]
        public void close(
            JObject config,
            ICallback doneCallback,
            ICallback errorCallback
        )
        {

            try
            {
                string dbname = config.Value<string>("path") ?? "";
                Database db = databases[dbname];
                db.closedb();
                databases.Remove(dbname);
                databaseKeys.Remove(dbname);
                doneCallback.Invoke();
                //Debug.WriteLine("Closed Database");
            }
            catch (Exception e)
            {
                errorCallback.Invoke(e.Message);
                Debug.WriteLine("Close database failed " + e.ToString());
            }

        }


        [ReactMethod]
        public async void backgroundExecuteSqlBatch(
            JObject config,
            ICallback doneCallback,
            ICallback errorCallback
        )
        {

            try
            {

                string dbname = config.Value<JObject>("dbargs").Value<string>("dbname") ?? "";

                if (!databaseKeys.Keys.Contains(dbname))
                {
                    throw new Exception("Database does not exist");
                }
                if (!databases.Keys.Contains(dbname))
                {

                    try
                    {
                        await reOpenDatabases();
                        if (!databases.Keys.Contains(dbname))
                        {
                            throw new Exception("Resume Didn't Work");
                        }
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Failed to reopendatabase" + e.ToString());
                    }
                }

                string executesBase64 = config.Value<string>("executes");
                byte[] data = Convert.FromBase64String(executesBase64);
                string executesBase64Decoded = Encoding.UTF8.GetString(data);
                JsonReader reader = new JsonTextReader(new StringReader(executesBase64Decoded));
                reader.DateParseHandling = DateParseHandling.None;
                JArray executes = JArray.Load(reader);

                Database db = databases[dbname];

                JArray results = new JArray();
                long totalChanges = db.TotalChanges;
                string q = "";
                foreach (JObject e in executes)
                {
                    try
                    {
                        q = e.Value<string>("qid");
                        string s = e.Value<string>("sql");
                        JArray pj = e.Value<JArray>("params");
                        IReadOnlyList<Object> p = pj.ToObject<IReadOnlyList<Object>>();
                        JArray rows = JArray.Parse(await db.AllAsyncVector(s, p));
                        long rowsAffected = db.TotalChanges - totalChanges;
                        totalChanges = db.TotalChanges;
                        JObject result = new JObject();
                        result["rowsAffected"] = rowsAffected;
                        result["rows"] = rows;
                        result["insertId"] = db.LastInsertRowId;
                        JObject resultInfo = new JObject();
                        resultInfo["type"] = "success";
                        resultInfo["qid"] = q;
                        resultInfo["result"] = result;
                        results.Add(resultInfo);
                    }
                    catch (Exception err)
                    {
                        JObject resultInfo = new JObject();
                        JObject result = new JObject();
                        result["code"] = -1;
                        result["message"] = err.Message;
                        resultInfo["type"] = "error";
                        resultInfo["qid"] = q;
                        resultInfo["result"] = result;
                        results.Add(resultInfo);
                    }
                }
                doneCallback.Invoke(results);
                //Debug.WriteLine("Done Execute Sql Batch");
            }
            catch (Exception e)
            {
                errorCallback.Invoke(e.Message);
                Debug.WriteLine("Error in background execute Sql batch" + e.ToString());
            }
            finally { }

        }

        [ReactMethod]
        public async void delete(
            JObject config,
            ICallback doneCallback,
            ICallback errorCallback)
        {

            try
            {
                string dbname = config.Value<string>("path") ?? "";
                if (databases.Keys.Contains(dbname))
                {
                    Database db = databases[dbname];
                    db.closedb();
                    databases.Remove(dbname);
                    databaseKeys.Remove(dbname);
                }
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(dbname);
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                doneCallback.Invoke();
                //Debug.WriteLine("Deleted database");
            }
            catch (Exception err)
            {
                errorCallback.Invoke(err.Message);
                Debug.WriteLine("Error in Delete Database " + err.ToString());
            }
            finally { }
        }

        public void OnSuspend()
        {
            OnDestroy();
        }

        public async void OnResume()
        {
            await reOpenDatabases();
        }

        public void OnDestroy()
        {
            // close all databases
            foreach (KeyValuePair<String, Database> entry in databases)
            {
                entry.Value.closedb();
            }
            databases.Clear();

        }

        public override void Initialize()
        {
            base.Initialize();
            Context.AddLifecycleEventListener(this);
        }

        async Task reOpenDatabases()
        {
            try
            {
                foreach (KeyValuePair<String, String> entry in databaseKeys)
                {
                    string opendbname = ApplicationData.Current.LocalFolder.Path + "\\" + entry.Key;
                    FileInfo fInfo = new FileInfo(opendbname);
                    if (!fInfo.Exists)
                    {
                        throw new Exception(opendbname + " not found");
                    }
                    Database db = await Database.OpenAsyncWithKey(opendbname, entry.Value);
                    if (version == null)
                    {
                        JObject result = JObject.Parse(await db.OneAsyncVector("SELECT sqlite_version() || ' (' || sqlite_source_id() || ')' as version", new List<string>()));
                        version = result.Value<string>("version");
                    }
                    databases[entry.Key] = db;

                }
            }
            catch (Exception e)
            {
                throw new Exception("Failed to restore database" + e.ToString());
            }
        }
    }

    public class SQLiteReactPackage : IReactPackage
    {
        public IReadOnlyList<INativeModule> CreateNativeModules(ReactContext reactContext)
        {
            return new List<INativeModule>
        {
            new SQLiteModule(reactContext)
        };
        }

        public IReadOnlyList<Type> CreateJavaScriptModulesConfig()
        {
            return new List<Type>(0);
        }

        public IReadOnlyList<UIManager.IViewManager> CreateViewManagers(
            ReactContext reactContext)
        {
            return new List<UIManager.IViewManager>(0);
        }

    }

}
