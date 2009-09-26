using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Collections;

using Indihiang.Data;
using Indihiang.Cores.Features;
namespace Indihiang.Cores
{
    public abstract class BaseLogParser
    {
        private static int TOTAL_PER_PROCESS = 100;

        //protected SpinLock _spinLock;
        protected List<BaseLogAnalyzeFeature> _features;
        protected List<BaseLogAnalyzeFeature> _paralleFeatures;
        private SynchronizationContext _synContext;
        private LazyInit<TaskManager> _taskManager;
        //private Task _mainTask;
        private ConcurrentQueue<FeatureDataRow> _featureQueue;
        private ConcurrentQueue<DumpLogData> _dumpLogQueue;
        private Thread _dataQueue;
        private bool _finish;
        private ManualResetEventSlim _exitDump;


        public event EventHandler<LogInfoEventArgs> ParseLogHandler;

        public string LogFile { get; set; }
        public string ParserID { get; set; }
        public EnumLogFile LogFileFormat { get; protected set; }
        public bool UseParallel { get; set; }        
        public List<BaseLogAnalyzeFeature> Features
        {
            get
            {
                return _features;
            }
            set
            {
                if (_features == value)
                    return;
                _features = value;
            }
        }
        //public bool isCompleted
        //{
        //    get
        //    {
        //        if (_mainTask == null)
        //            return true;

        //        return _mainTask.IsCompleted;
        //    }           
        //}
        public List<BaseLogAnalyzeFeature> ParalleFeatures
        {
            get
            {
                return _paralleFeatures;
            }
            set
            {
                if (_paralleFeatures == value)
                    return;
                _paralleFeatures = value;
            }
        }

        protected BaseLogParser(string logFile, EnumLogFile logFileFormat)
        {            
            LogFile = logFile;
            LogFileFormat = logFileFormat;

            Initilaize();           
        }

        private void Initilaize()
        {
            //_spinLock = new SpinLock();
            _features = new List<BaseLogAnalyzeFeature>();
            _paralleFeatures = new List<BaseLogAnalyzeFeature>();
            _synContext = AsyncOperationManager.SynchronizationContext;

            _taskManager = new LazyInit<TaskManager>(() => new TaskManager(
                              new TaskManagerPolicy(1, Environment.ProcessorCount)),
                              LazyInitMode.AllowMultipleExecution);

            _featureQueue = new ConcurrentQueue<FeatureDataRow>();
            _dumpLogQueue = new ConcurrentQueue<DumpLogData>();
        }

        protected virtual void OnParseLog(LogInfoEventArgs e)
        {
            if (ParseLogHandler != null)
                ParseLogHandler(this, e);

            Debug.WriteLine(String.Format("OnParseLog:: {0}", e.Message));
        }

        public bool Parse()
        {
            bool success = false;
            if (LogFile.StartsWith("--"))
            {
                if (_dataQueue == null)
                    _dataQueue = new Thread(DumpData);
                
                _finish = false;
                _dataQueue.IsBackground = true;
                _dataQueue.Start();

                string tmp = LogFile.Substring(2);
                string[] files = tmp.Split(new char[] { ';' });
                List<string> listFiles = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {
                    if (!string.IsNullOrEmpty(files[i]))
                        if (!listFiles.Contains(files[i].ToLower().Trim()))
                            listFiles.Add(files[i].ToLower().Trim());
                }

                //Dictionary<string, List<BaseLogAnalyzeFeature>> resultData = new Dictionary<string, List<BaseLogAnalyzeFeature>>();
                if (UseParallel)
                {
                    #region Parallel Processing

                    success = ParallelParse(success, listFiles);

                    #endregion

                }
                else
                {
                    #region Non Parallel Processing
                    //success = NonParallelParse(success, listFiles);
                    #endregion

                }

                //try
                //{
                //    //_spinLock.Enter();
                //    foreach (KeyValuePair<string, List<BaseLogAnalyzeFeature>> item in resultData)
                //    {
                //        //List<BaseLogAnalyzeFeature> items = resultData[i].Value;

                //        for (int i = 0; i < item.Value.Count; i++)
                //        {
                //            for (int j = 0; j < _paralleFeatures.Count; j++)
                //                if (_paralleFeatures[j].FeatureName == item.Value[i].FeatureName)
                //                    _paralleFeatures[j].SynchData(item.Value[i].Items);

                //            //item.Value.ForEach(delegate(BaseLogAnalyzeFeature f)
                //            //{
                //            //    for (int j = 0; j < _paralleFeatures.Count; j++)
                //            //        if (_paralleFeatures[j].FeatureName == f.FeatureName)
                //            //            _paralleFeatures[j].SynchData(f.Items);
                //            //});
                //        }
                //        //items.Clear();
                //        //resultData[i].Dispose();
                //    }
                //    //_spinLock.Exit();
                //}
                //catch (Exception err)
                //{
                //    #region Handle Exception
                //    LogInfoEventArgs logInfo = new LogInfoEventArgs(
                //       ParserID,
                //       EnumLogFile.UNKNOWN,
                //       LogProcessStatus.SUCCESS,
                //       "Parse()",
                //       String.Format("Internal Error-Synch: {0}", err.Message));
                //    _synContext.Post(OnParseLog, logInfo);
                //    logInfo = new LogInfoEventArgs(
                //           ParserID,
                //           EnumLogFile.UNKNOWN,
                //           LogProcessStatus.SUCCESS,
                //           "Parse()",
                //           String.Format("Source Internal Error-Synch: {0}", err.Source));
                //    _synContext.Post(OnParseLog, logInfo);
                //    logInfo = new LogInfoEventArgs(
                //           ParserID,
                //           EnumLogFile.UNKNOWN,
                //           LogProcessStatus.SUCCESS,
                //           "Parse()",
                //           String.Format("Detail Internal Error-Synch: {0}", err.StackTrace));
                //    _synContext.Post(OnParseLog, logInfo);

                //    #endregion

                //}


            }
            else
            {
                if (UseParallel)
                    ParseLogFile(_paralleFeatures, LogFile);
                else
                    ParseLogFile(_features, LogFile);
            }
            _finish = true;
            Thread.Sleep(100);
            ExitDumpThread();

            return success;
        }

        private bool NonParallelParse(bool success, List<string> listFiles, Dictionary<string, List<BaseLogAnalyzeFeature>> resultData)
        {
            for (int i = 0; i < listFiles.Count; i++)
            {
                try
                {


                    if (!string.IsNullOrEmpty(listFiles[i]))
                    {
                        List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateFeatures(LogFileFormat);
                        resultData.Add(listFiles[i], ParseLogFile(features, listFiles[i]));
                        //resultData.Add(listFiles[i], ParseLogFile(_features, listFiles[i]));
                    }
                }
                catch (Exception err)
                {
                    #region Handle Exception
                    LogInfoEventArgs logInfo = new LogInfoEventArgs(
                       ParserID,
                       EnumLogFile.UNKNOWN,
                       LogProcessStatus.SUCCESS,
                       "ParseLogFile()",
                       String.Format("Internal Error: {0}", err.Message));
                    _synContext.Post(OnParseLog, logInfo);
                    logInfo = new LogInfoEventArgs(
                           ParserID,
                           EnumLogFile.UNKNOWN,
                           LogProcessStatus.SUCCESS,
                           "ParseLogFile()",
                           String.Format("Source Internal Error: {0}", err.Source));
                    _synContext.Post(OnParseLog, logInfo);
                    logInfo = new LogInfoEventArgs(
                           ParserID,
                           EnumLogFile.UNKNOWN,
                           LogProcessStatus.SUCCESS,
                           "ParseLogFile()",
                           String.Format("Detail Internal Error: {0}", err.StackTrace));
                    _synContext.Post(OnParseLog, logInfo);

                    #endregion
                }
            }
            success = true;
            return success;
        }

        private bool ParallelParse(bool success, List<string> listFiles)
        {
            try
            {
                List<ManualResetEventSlim> resets = new List<ManualResetEventSlim>();

                int i = 0;                
                Parallel.ForEach<string>(listFiles, file =>
                {
                    LogInfoEventArgs logInfo = new LogInfoEventArgs(
                                   ParserID,
                                   EnumLogFile.UNKNOWN,
                                   LogProcessStatus.SUCCESS,
                                   "Parse()",
                                   String.Format("Run Parse on file {0}", file));
                    _synContext.Post(OnParseLog, logInfo);


                    ManualResetEventSlim obj = new ManualResetEventSlim(false);
                    resets.Add(obj);
                    try
                    {
                        RunParse(file);

                        logInfo = new LogInfoEventArgs(
                                   ParserID,
                                   EnumLogFile.UNKNOWN,
                                   LogProcessStatus.SUCCESS,
                                   "Parse()",
                                   String.Format("Run Parse : {0} was done", file));
                        _synContext.Post(OnParseLog, logInfo);
                    }
                    catch (Exception err)
                    {
                        logInfo = new LogInfoEventArgs(
                                   ParserID,
                                   EnumLogFile.UNKNOWN,
                                   LogProcessStatus.SUCCESS,
                                   "Parse()",
                                   String.Format("Error occurred on file {0}==>{1}\r\n{2}", file, err.Message, err.StackTrace));
                        _synContext.Post(OnParseLog, logInfo);
                    }
                    obj.Set();



                });


                try
                {
                    for (i = 0; i < resets.Count; i++)
                    {
                        if (!resets[i].IsSet)
                            resets[i].Wait();
                    }
                }
                catch
                {
                }
                _finish = true;


                Thread.Sleep(100);
                success = true;

            }
            catch (AggregateException err)
            {
                #region Handle Exception
                LogInfoEventArgs logInfo = new LogInfoEventArgs(
                   ParserID,
                   EnumLogFile.UNKNOWN,
                   LogProcessStatus.SUCCESS,
                   "Parse()",
                   String.Format("Internal Error: {0}", err.Message));
                _synContext.Post(OnParseLog, logInfo);
                logInfo = new LogInfoEventArgs(
                       ParserID,
                       EnumLogFile.UNKNOWN,
                       LogProcessStatus.SUCCESS,
                       "Parse()",
                       String.Format("Source Internal Error: {0}", err.Source));
                _synContext.Post(OnParseLog, logInfo);
                logInfo = new LogInfoEventArgs(
                       ParserID,
                       EnumLogFile.UNKNOWN,
                       LogProcessStatus.SUCCESS,
                       "Parse()",
                       String.Format("Detail Internal Error: {0}", err.StackTrace));
                _synContext.Post(OnParseLog, logInfo);
                logInfo = new LogInfoEventArgs(
                       ParserID,
                       EnumLogFile.UNKNOWN,
                       LogProcessStatus.SUCCESS,
                       "Parse()",
                       String.Format("Internal Exception Error: {0}", err.InnerException.Message));
                _synContext.Post(OnParseLog, logInfo);

                #endregion

            }
            catch (Exception err)
            {
                #region Handle Exception
                LogInfoEventArgs logInfo = new LogInfoEventArgs(
                   ParserID,
                   EnumLogFile.UNKNOWN,
                   LogProcessStatus.SUCCESS,
                   "Parse()",
                   String.Format("Internal Error: {0}", err.Message));
                _synContext.Post(OnParseLog, logInfo);
                logInfo = new LogInfoEventArgs(
                       ParserID,
                       EnumLogFile.UNKNOWN,
                       LogProcessStatus.SUCCESS,
                       "Parse()",
                       String.Format("Source Internal Error: {0}", err.Source));
                _synContext.Post(OnParseLog, logInfo);
                logInfo = new LogInfoEventArgs(
                       ParserID,
                       EnumLogFile.UNKNOWN,
                       LogProcessStatus.SUCCESS,
                       "Parse()",
                       String.Format("Detail Internal Error: {0}", err.StackTrace));
                _synContext.Post(OnParseLog, logInfo);

                #endregion

            }
            return success;
        }

        private void ExitDumpThread()
        {
            if (_dataQueue != null)
            {
                if (_dataQueue.IsAlive)
                {
                    _dataQueue.Join(1000);
                    try
                    {
                        if (_dataQueue.IsAlive)
                            _dataQueue.Abort();
                    }
                    catch (Exception) { }
                }
            }
        }
        private void DumpData()
        {
            List<BaseFeature> features = IndihiangHelper.GenerateIndihiangFeatures(new Guid(ParserID), LogFileFormat);
            //List<string> fields = IndihiangHelper.MergeFields(features);

            _exitDump = new ManualResetEventSlim(false);            
            while (!_finish)
            {
                DumpLogData row;
                try
                {
                    if (_dumpLogQueue.TryDequeue(out row))
                    {
                        int index = row.Header.IndexOf("date");
                        foreach (KeyValuePair<string, List<string>> item in row.Rows)
                        {
                            string data = item.Value[index];
                            string file = IndihiangHelper.GetIndihiangFile(data, ParserID);

                            DataHelper helper = null;
                            if (!File.Exists(file))
                            {
                                IndihiangHelper.CopyLogDB(file);
                                helper = new DataHelper(file);

                                for (int i = 0; i < features.Count; i++)
                                {
                                    for (int j = 0; j < features[i].FeatureFields.Count; j++)
                                        helper.InsertFeature(features[i].FeatureName.ToString(), features[i].FeatureFields[j]);                                    
                                }
                            }

                            if (helper!=null)
                                helper = new DataHelper(file);

                            int id = helper.GetSharedId("date", data);
                            if (id <= 0)
                            {
                                helper.InsertShared("date", data);
                                id = helper.GetSharedId("date", data);
                            }
                            for (int i = 0; i < features.Count; i++)
                            {
                                features[i].DBObject = helper;
                                //features[i].Parse(row.Header, item.Value);
                            }
                            
                        }

                        //row.
                        //string file = IndihiangHelper.GetIndihiangFile()
                        //DataHelper helper = new DataHelper()

                        //DataHelper helper = new DataHelper(IndihiangHelper.GetIndihiangFile(row.DateData, LogParserId.ToString()));
                        //foreach (KeyValuePair<string, string> item in row.Rows)
                        //{
                        //    helper.InsertFeatureData(row.FeatureName, item.Key, item.Value);
                        //}
                    }
                    else
                        Thread.Sleep(100);
                }
                catch (Exception) { }
            }
            _exitDump.Set();
        }

        #region backup parse()
        //public bool Parse()
        //{
        //    bool success = false;
        //    if (LogFile.StartsWith("--"))
        //    {
        //        string tmp = LogFile.Substring(2);
        //        string[] files = tmp.Split(new char[] { ';' });
        //        List<string> listFiles = new List<string>();
        //        for (int i = 0; i < files.Length; i++)
        //        {
        //            if (!string.IsNullOrEmpty(files[i]))
        //                if (!listFiles.Contains(files[i].ToLower().Trim()))
        //                    listFiles.Add(files[i].ToLower().Trim());
        //        }

        //        Dictionary<string, List<BaseLogAnalyzeFeature>> resultData = new Dictionary<string, List<BaseLogAnalyzeFeature>>();
        //        if (UseParallel)
        //        {
        //            #region Parallel Processing

        //            try
        //            {
        //                List<ManualResetEventSlim> resets = new List<ManualResetEventSlim>();                        

        //                int i = 0;
        //                //List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateParallelFeatures(new Guid(ParserID), LogFileFormat);
        //                _paralleFeatures = IndihiangHelper.GenerateParallelFeatures(new Guid(ParserID), LogFileFormat);
        //                Parallel.ForEach<BaseLogAnalyzeFeature>(_paralleFeatures, feature =>
        //                {
        //                    LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //                                   ParserID,
        //                                   EnumLogFile.UNKNOWN,
        //                                   LogProcessStatus.SUCCESS,
        //                                   "Parse()",
        //                                   String.Format("Run Parse : {0}", feature.FeatureName.ToString()));
        //                    _synContext.Post(OnParseLog, logInfo);

                            
        //                    ManualResetEventSlim obj = new ManualResetEventSlim(false);
        //                    resets.Add(obj);
        //                    for (int j = 0; j < listFiles.Count; j++)
        //                    {
        //                        RunParse(feature, listFiles[j]);                                
        //                    }
        //                    obj.Set();

        //                    logInfo = new LogInfoEventArgs(
        //                                   ParserID,
        //                                   EnumLogFile.UNKNOWN,
        //                                   LogProcessStatus.SUCCESS,
        //                                   "Parse()",
        //                                   String.Format("Run Parse : {0} was done", feature.FeatureName.ToString()));
        //                    _synContext.Post(OnParseLog, logInfo);

        //                });
                        

        //                //Parallel.ForEach<string>(listFiles, file =>
        //                //{
        //                //    if (!string.IsNullOrEmpty(file))
        //                //    {
        //                //        ManualResetEventSlim obj = new ManualResetEventSlim(false);
        //                //        resets.Add(obj);

        //                //        List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateParallelFeatures(new Guid(ParserID),LogFileFormat);
        //                //        resultData.Add(file, ParseLogFile(features, file));

        //                //        obj.Set();
                                
        //                //    }
        //                //});

        //                try
        //                {
        //                    for (i = 0; i < resets.Count; i++)
        //                    {
        //                        if (!resets[i].IsSet)
        //                            resets[i].Wait();
        //                    }
        //                }
        //                catch
        //                {                           
        //                } 


        //                //WaitHandle.WaitAll(resets);
        //                //_mainTask = Task.Create(
        //                //        delegate
        //                //        {
        //                //            //for (int i = 0; i < listFiles.Count; i++)
        //                //            //{
        //                //            //    List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateParallelFeatures(LogFileFormat);
        //                //            //    resultData[i] = Future.Create(
        //                //            //                   () => ParseLogFile(features, listFiles[i])
        //                //            //                );
        //                //            //    //if (resultData[i] != null)
        //                //            //    //    resultData[i].Wait();

                                        
        //                //            //}
        //                //            //Task.WaitAll(resultData);
        //                //            Parallel.ForEach(listFiles, file => 
        //                //            {
        //                //                List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateParallelFeatures(LogFileFormat);
        //                //                resultData.Add(file,ParseLogFile(features, file));
        //                //            });
        //                //            //Parallel.For(0, listFiles.Count, index =>
        //                //            //    {
        //                //            //        List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateParallelFeatures(LogFileFormat);
        //                //            //        resultData[index] = Future.Create(
        //                //            //                       () => ParseLogFile(features, listFiles[index])
        //                //            //                    );
        //                //            //        //if (resultData[index] != null)
        //                //            //        //    resultData[index].Wait();
        //                //            //    });
     
        //                //        },
        //                //        _taskManager.Value,
        //                //        TaskCreationOptions.None
        //                //     );

        //                //try
        //                //{

        //                //    _mainTask.Wait();
        //                //}
        //                //catch (AggregateException err)
        //                //{
        //                //    #region Handle Exception
        //                //    LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //                //       ParserID,
        //                //       EnumLogFile.UNKNOWN,
        //                //       LogProcessStatus.SUCCESS,
        //                //       "Parse()",
        //                //       String.Format("Internal Error-inner: {0}", err.Message));
        //                //    _synContext.Post(OnParseLog, logInfo);
        //                //    logInfo = new LogInfoEventArgs(
        //                //           ParserID,
        //                //           EnumLogFile.UNKNOWN,
        //                //           LogProcessStatus.SUCCESS,
        //                //           "Parse()",
        //                //           String.Format("Source Internal Error-inner: {0}", err.Source));
        //                //    _synContext.Post(OnParseLog, logInfo);
        //                //    logInfo = new LogInfoEventArgs(
        //                //           ParserID,
        //                //           EnumLogFile.UNKNOWN,
        //                //           LogProcessStatus.SUCCESS,
        //                //           "Parse()",
        //                //           String.Format("Detail Internal Error-inner: {0}", err.StackTrace));
        //                //    _synContext.Post(OnParseLog, logInfo);
        //                //    logInfo = new LogInfoEventArgs(
        //                //           ParserID,
        //                //           EnumLogFile.UNKNOWN,
        //                //           LogProcessStatus.SUCCESS,
        //                //           "Parse()",
        //                //           String.Format("Internal Exception Error-inner: {0}", err.InnerException.Message));
        //                //    _synContext.Post(OnParseLog, logInfo);

        //                //    #endregion

        //                //}
        //                //catch (Exception err)
        //                //{
        //                //    #region Handle Exception
        //                //    LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //                //       ParserID,
        //                //       EnumLogFile.UNKNOWN,
        //                //       LogProcessStatus.SUCCESS,
        //                //       "Parse()",
        //                //       String.Format("Internal Error-inner: {0}", err.Message));
        //                //    _synContext.Post(OnParseLog, logInfo);
        //                //    logInfo = new LogInfoEventArgs(
        //                //           ParserID,
        //                //           EnumLogFile.UNKNOWN,
        //                //           LogProcessStatus.SUCCESS,
        //                //           "Parse()",
        //                //           String.Format("Source Internal Error-inner: {0}", err.Source));
        //                //    _synContext.Post(OnParseLog, logInfo);
        //                //    logInfo = new LogInfoEventArgs(
        //                //           ParserID,
        //                //           EnumLogFile.UNKNOWN,
        //                //           LogProcessStatus.SUCCESS,
        //                //           "Parse()",
        //                //           String.Format("Detail Internal Error-inner: {0}", err.StackTrace));
        //                //    _synContext.Post(OnParseLog, logInfo);

        //                //    #endregion

        //                //}                       
        //                //for (int i = 0; i < resultData.Count; i++)
        //                //{
        //                //    List<BaseLogAnalyzeFeature> items = resultData[i].Value;
        //                //    items.ForEach(delegate(BaseLogAnalyzeFeature item)
        //                //    {
        //                //        for (int j = 0; j < _paralleFeatures.Count; j++)
        //                //            if (_paralleFeatures[j].FeatureName == item.FeatureName)
        //                //                _paralleFeatures[j].SynchData(item.Items);
        //                //    });

        //                //    items.Clear();
        //                //    resultData[i].Dispose();
        //                //}
        //                Thread.Sleep(100);
        //                success = true;

        //            }
        //            catch (AggregateException err)
        //            {
        //                #region Handle Exception
        //                LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //                   ParserID,
        //                   EnumLogFile.UNKNOWN,
        //                   LogProcessStatus.SUCCESS,
        //                   "Parse()",
        //                   String.Format("Internal Error: {0}", err.Message));
        //                _synContext.Post(OnParseLog, logInfo);
        //                logInfo = new LogInfoEventArgs(
        //                       ParserID,
        //                       EnumLogFile.UNKNOWN,
        //                       LogProcessStatus.SUCCESS,
        //                       "Parse()",
        //                       String.Format("Source Internal Error: {0}", err.Source));
        //                _synContext.Post(OnParseLog, logInfo);
        //                logInfo = new LogInfoEventArgs(
        //                       ParserID,
        //                       EnumLogFile.UNKNOWN,
        //                       LogProcessStatus.SUCCESS,
        //                       "Parse()",
        //                       String.Format("Detail Internal Error: {0}", err.StackTrace));
        //                _synContext.Post(OnParseLog, logInfo);
        //                logInfo = new LogInfoEventArgs(
        //                       ParserID,
        //                       EnumLogFile.UNKNOWN,
        //                       LogProcessStatus.SUCCESS,
        //                       "Parse()",
        //                       String.Format("Internal Exception Error: {0}", err.InnerException.Message));
        //                _synContext.Post(OnParseLog, logInfo);

        //                #endregion

        //            }
        //            catch (Exception err)
        //            {
        //                #region Handle Exception
        //                LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //                   ParserID,
        //                   EnumLogFile.UNKNOWN,
        //                   LogProcessStatus.SUCCESS,
        //                   "Parse()",
        //                   String.Format("Internal Error: {0}", err.Message));
        //                _synContext.Post(OnParseLog, logInfo);
        //                logInfo = new LogInfoEventArgs(
        //                       ParserID,
        //                       EnumLogFile.UNKNOWN,
        //                       LogProcessStatus.SUCCESS,
        //                       "Parse()",
        //                       String.Format("Source Internal Error: {0}", err.Source));
        //                _synContext.Post(OnParseLog, logInfo);
        //                logInfo = new LogInfoEventArgs(
        //                       ParserID,
        //                       EnumLogFile.UNKNOWN,
        //                       LogProcessStatus.SUCCESS,
        //                       "Parse()",
        //                       String.Format("Detail Internal Error: {0}", err.StackTrace));
        //                _synContext.Post(OnParseLog, logInfo);

        //                #endregion

        //            }

        //            #endregion

        //        }
        //        else
        //        {
        //            #region Non Parallel Processing
        //            for (int i = 0; i < listFiles.Count; i++)
        //            {
        //                try
        //                {


        //                    if (!string.IsNullOrEmpty(listFiles[i]))
        //                    {
        //                        List<BaseLogAnalyzeFeature> features = IndihiangHelper.GenerateFeatures(LogFileFormat);
        //                        resultData.Add(listFiles[i], ParseLogFile(features, listFiles[i]));
        //                        //resultData.Add(listFiles[i], ParseLogFile(_features, listFiles[i]));
        //                    }
        //                }
        //                catch (Exception err)
        //                {
        //                    #region Handle Exception
        //                    LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //                       ParserID,
        //                       EnumLogFile.UNKNOWN,
        //                       LogProcessStatus.SUCCESS,
        //                       "ParseLogFile()",
        //                       String.Format("Internal Error: {0}", err.Message));
        //                    _synContext.Post(OnParseLog, logInfo);
        //                    logInfo = new LogInfoEventArgs(
        //                           ParserID,
        //                           EnumLogFile.UNKNOWN,
        //                           LogProcessStatus.SUCCESS,
        //                           "ParseLogFile()",
        //                           String.Format("Source Internal Error: {0}", err.Source));
        //                    _synContext.Post(OnParseLog, logInfo);
        //                    logInfo = new LogInfoEventArgs(
        //                           ParserID,
        //                           EnumLogFile.UNKNOWN,
        //                           LogProcessStatus.SUCCESS,
        //                           "ParseLogFile()",
        //                           String.Format("Detail Internal Error: {0}", err.StackTrace));
        //                    _synContext.Post(OnParseLog, logInfo);

        //                    #endregion
        //                }
        //            }
        //            success = true;
        //            #endregion

        //        }

        //        try
        //        {
        //            //_spinLock.Enter();
        //            foreach (KeyValuePair<string, List<BaseLogAnalyzeFeature>> item in resultData)
        //            {
        //                //List<BaseLogAnalyzeFeature> items = resultData[i].Value;

        //                for (int i = 0; i < item.Value.Count; i++)
        //                {
        //                    for (int j = 0; j < _paralleFeatures.Count; j++)
        //                        if (_paralleFeatures[j].FeatureName == item.Value[i].FeatureName)
        //                            _paralleFeatures[j].SynchData(item.Value[i].Items);

        //                    //item.Value.ForEach(delegate(BaseLogAnalyzeFeature f)
        //                    //{
        //                    //    for (int j = 0; j < _paralleFeatures.Count; j++)
        //                    //        if (_paralleFeatures[j].FeatureName == f.FeatureName)
        //                    //            _paralleFeatures[j].SynchData(f.Items);
        //                    //});
        //                }
        //                //items.Clear();
        //                //resultData[i].Dispose();
        //            }
        //            //_spinLock.Exit();
        //        }
        //        catch (Exception err)
        //        {
        //            #region Handle Exception
        //            LogInfoEventArgs logInfo = new LogInfoEventArgs(
        //               ParserID,
        //               EnumLogFile.UNKNOWN,
        //               LogProcessStatus.SUCCESS,
        //               "Parse()",
        //               String.Format("Internal Error-Synch: {0}", err.Message));
        //            _synContext.Post(OnParseLog, logInfo);
        //            logInfo = new LogInfoEventArgs(
        //                   ParserID,
        //                   EnumLogFile.UNKNOWN,
        //                   LogProcessStatus.SUCCESS,
        //                   "Parse()",
        //                   String.Format("Source Internal Error-Synch: {0}", err.Source));
        //            _synContext.Post(OnParseLog, logInfo);
        //            logInfo = new LogInfoEventArgs(
        //                   ParserID,
        //                   EnumLogFile.UNKNOWN,
        //                   LogProcessStatus.SUCCESS,
        //                   "Parse()",
        //                   String.Format("Detail Internal Error-Synch: {0}", err.StackTrace));
        //            _synContext.Post(OnParseLog, logInfo);

        //            #endregion

        //        }


        //    }
        //    else
        //    {
        //        if (UseParallel)
        //            ParseLogFile(_paralleFeatures, LogFile);
        //        else
        //            ParseLogFile(_features, LogFile);
        //    }

        //    return success;

        //}
        #endregion

        protected List<BaseLogAnalyzeFeature> ParseLogFile(List<BaseLogAnalyzeFeature> features,string logFile)
        {
            if (features == null)
                return null;

            //if (UseParallel)
            //{
                #region Logging
                LogInfoEventArgs logInfo = new LogInfoEventArgs(
                   ParserID,
                   EnumLogFile.UNKNOWN,
                   LogProcessStatus.SUCCESS,
                   "ParseLogFile()",
                   String.Format("Read File: {0}", logFile));
                _synContext.Post(OnParseLog, logInfo);

                Debug.WriteLine(String.Format("Read File: {0}", logFile));
                #endregion

                foreach (var feature in features)
                {
                    using (StreamReader sr = new StreamReader(logFile))
                    {                       
                        //try
                        //{

                            string line = sr.ReadLine();
                            //Debug.WriteLine(String.Format("Indihiang Read: {0}", line));

                            List<string> currentHeader = new List<string>();
                            while (!string.IsNullOrEmpty(line))
                            {
                                //Debug.WriteLine(String.Format("Read: {0}", line));
                                if (IsLogHeader(line))
                                {
                                    List<string> list1 = ParseHeader(line);
                                    if (list1 != null)
                                        currentHeader = new List<string>(list1);
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(line))
                                    {
                                        string[] rows = line.Split(new char[] { ' ' });
                                        if (rows != null)
                                        {
                                            if (rows.Length > 0)
                                            {
                                                feature.Parse(currentHeader, rows);
                                            }
                                        }
                                    }
                                }

                                line = sr.ReadLine();
                                if (!string.IsNullOrEmpty(line))
                                    line = line.Trim();
                                //if (!string.IsNullOrEmpty(line))
                                //    line = line.TrimEnd('\0');

                            }

                        //}
                        //catch (AggregateException err)
                        //{
                        //    #region Handle Exception
                        //    LogInfoEventArgs logInfo = new LogInfoEventArgs(
                        //       ParserID,
                        //       EnumLogFile.UNKNOWN,
                        //       LogProcessStatus.SUCCESS,
                        //       "Parse()",
                        //       String.Format("Internal Error: {0}", err.Message));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    logInfo = new LogInfoEventArgs(
                        //           ParserID,
                        //           EnumLogFile.UNKNOWN,
                        //           LogProcessStatus.SUCCESS,
                        //           "Parse()",
                        //           String.Format("Source Internal Error: {0}", err.Source));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    logInfo = new LogInfoEventArgs(
                        //           ParserID,
                        //           EnumLogFile.UNKNOWN,
                        //           LogProcessStatus.SUCCESS,
                        //           "Parse()",
                        //           String.Format("Detail Internal Error: {0}", err.StackTrace));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    logInfo = new LogInfoEventArgs(
                        //           ParserID,
                        //           EnumLogFile.UNKNOWN,
                        //           LogProcessStatus.SUCCESS,
                        //           "Parse()",
                        //           String.Format("Internal Exception Error: {0}", err.InnerException.Message));
                        //    _synContext.Post(OnParseLog, logInfo);

                        //    #endregion

                        //}
                        //catch (ArgumentOutOfRangeException err)
                        //{
                        //    #region Logging
                        //    System.Diagnostics.Debug.WriteLine(err.Message);
                        //    logInfo = new LogInfoEventArgs(
                        //          ParserID,
                        //          EnumLogFile.UNKNOWN,
                        //          LogProcessStatus.FAILED,
                        //          "ParseLogFile()",
                        //          String.Format("Internal Error on ParseLogFile: {0}", err.Message));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    logInfo = new LogInfoEventArgs(
                        //          ParserID,
                        //          EnumLogFile.UNKNOWN,
                        //          LogProcessStatus.FAILED,
                        //          "ParseLogFile()",
                        //          String.Format("Detail: {0} ", err.StackTrace));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    #endregion
                        //}
                        //catch (Exception err)
                        //{
                        //    #region Logging
                        //    System.Diagnostics.Debug.WriteLine(err.Message);
                        //    logInfo = new LogInfoEventArgs(
                        //          ParserID,
                        //          EnumLogFile.UNKNOWN,
                        //          LogProcessStatus.FAILED,
                        //          "ParseLogFile()",
                        //          String.Format("Internal Error on ParseLogFile: {0}", err.Message));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    logInfo = new LogInfoEventArgs(
                        //          ParserID,
                        //          EnumLogFile.UNKNOWN,
                        //          LogProcessStatus.FAILED,
                        //          "ParseLogFile()",
                        //          String.Format("Detail: {0} ", err.StackTrace));
                        //    _synContext.Post(OnParseLog, logInfo);
                        //    #endregion
                        //}
                       
                    }                    
                }

                #region Logging
                logInfo = new LogInfoEventArgs(
                          ParserID,
                          EnumLogFile.UNKNOWN,
                          LogProcessStatus.SUCCESS,
                          "ParseLogFile()",
                          String.Format("Read File: {0} is done", logFile));
                _synContext.Post(OnParseLog, logInfo);
                #endregion
            //}

            return features;

        }

        //protected void RunParse(BaseLogAnalyzeFeature feature, string logFile)
        //{
        //    if (feature == null)
        //        return;


        //    Dictionary<string, List<string>> dictRows = new Dictionary<string, List<string>>();
        //    using (StreamReader sr = new StreamReader(File.Open(logFile,FileMode.Open,FileAccess.Read,FileShare.Read)))
        //    {               
        //        string line = sr.ReadLine();

        //        List<string> currentHeader = new List<string>();
        //        while (!string.IsNullOrEmpty(line))
        //        {
        //            if (IsLogHeader(line))
        //            {
        //                List<string> list1 = ParseHeader(line);
        //                if (list1 != null)
        //                    currentHeader = new List<string>(list1);
        //            }
        //            else
        //            {
        //                if (!string.IsNullOrEmpty(line))
        //                {
        //                    string[] rows = line.Split(new char[] { ' ' });
        //                    if (rows != null)
        //                    {
        //                        if (rows.Length > 0)
        //                        {
        //                            dictRows.Add(Guid.NewGuid().ToString(), new List<string>(rows));                                    
        //                        }
        //                    }
        //                }
        //            }

        //            line = sr.ReadLine();
        //            if (!string.IsNullOrEmpty(line))
        //                line = line.Trim();
        //        }
        //    }
        //}

        protected void RunParse(string logFile)
        {

            Dictionary<string, List<string>> dictRows = new Dictionary<string, List<string>>();
            using (StreamReader sr = new StreamReader(File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                string line = sr.ReadLine();

                List<string> currentHeader = new List<string>();
                while (!string.IsNullOrEmpty(line))
                {
                    if (IsLogHeader(line))
                    {
                        #region Parse Header
                        List<string> list1 = ParseHeader(line);
                        if (list1 != null)
                        {
                            if (currentHeader.Count > 0)
                            {
                                DumpLogData dumpLog = new DumpLogData
                                {
                                    Header = currentHeader,
                                    Rows = new Dictionary<string, List<string>>(dictRows)
                                };
                                _dumpLogQueue.Enqueue(dumpLog);
                                dictRows.Clear();
                            }
                            currentHeader = new List<string>(list1);
                        }
                        #endregion
                    }
                    else
                    {
                        #region Parse Data
                        if (!string.IsNullOrEmpty(line))
                        {
                            string[] rows = line.Split(new char[] { ' ' });
                            if (rows != null)
                            {
                                if (rows.Length > 0)
                                {
                                    dictRows.Add(Guid.NewGuid().ToString(), new List<string>(rows));
                                    if (dictRows.Keys.Count >= TOTAL_PER_PROCESS)
                                    {                                        
                                        DumpLogData dumpLog = new DumpLogData { 
                                            Header = currentHeader, 
                                            Rows = new Dictionary<string, List<string>>(dictRows) 
                                        };
                                        _dumpLogQueue.Enqueue(dumpLog);

                                        dictRows.Clear();
                                    }
                                }
                            }
                        }
                        #endregion
                    }

                    line = sr.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                        line = line.Trim();
                }
            }
        }

        //Backup
        //protected void RunParse(List<BaseFeature> features, string logFile)
        //{
        //    if (features == null)
        //        return;

        //    using (StreamReader sr = new StreamReader(File.Open(logFile, FileMode.Open, FileAccess.Read, FileShare.Read)))
        //    {
        //        string line = sr.ReadLine();

        //        List<string> currentHeader = new List<string>();
        //        while (!string.IsNullOrEmpty(line))
        //        {
        //            if (IsLogHeader(line))
        //            {
        //                List<string> list1 = ParseHeader(line);
        //                if (list1 != null)
        //                    currentHeader = new List<string>(list1);
        //            }
        //            else
        //            {
        //                if (!string.IsNullOrEmpty(line))
        //                {
        //                    string[] rows = line.Split(new char[] { ' ' });
        //                    if (rows != null)
        //                    {
        //                        if (rows.Length > 0)
        //                        {
        //                            feature.Parse(currentHeader, rows);
        //                        }
        //                    }
        //                }
        //            }

        //            line = sr.ReadLine();
        //            if (!string.IsNullOrEmpty(line))
        //                line = line.Trim();
        //        }
        //    }
        //}


        protected abstract bool IsLogHeader(string line);
        protected abstract List<string> ParseHeader(string line);
    }
}