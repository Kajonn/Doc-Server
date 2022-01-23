namespace Doc_Server
{

    internal delegate UploadResult UploadDelegate(UploadRequest request);

    internal class UploadManager
    {
        public struct Upload
        {
            public UploadStatus status;
            public UploadRequest request;
        }
        
        private readonly ReaderWriterLockSlim _readWriteLock = new();

        private readonly Dictionary<UInt64, Upload> _uploads = new();

        private readonly Queue<UInt64> _pendingUploads = new();
        private readonly Dictionary<UInt64, Task> _ongoingTasks = new();

        private UInt64 _nextuploadId = 0;

        private readonly UploadDelegate _uploadFunction;
        private readonly int _maxTasks;
        private readonly ILogger<UploadManager> _logger;

        public UploadManager(UploadDelegate uploadDelegate, int maxTasks, ILogger<UploadManager> logger_)
        {
            this._uploadFunction = uploadDelegate;
            this._maxTasks = maxTasks;
            _logger = logger_;
            _logger.LogInformation("Creating uploadManager with " + maxTasks + " threads");
        }

        private void TryBeginNextTask()
        {            
            _readWriteLock.EnterWriteLock();
            try {
                _logger.LogDebug("Try to begin next task, num pendingUploads: " + _pendingUploads.Count);

                while (_pendingUploads.Count > 0 && _ongoingTasks.Count < _maxTasks)
                {
                    UInt64 id = _pendingUploads.Dequeue();
                    _logger.LogInformation("Dequee request " + id + " remaining pendingUploads: " + _pendingUploads.Count);                    

                    Upload upload;
                    if (_uploads.TryGetValue(id, out upload))
                    {
                        //TODO use progress and cancelation tokens to controll and monitor the task
                        var uploadTask = new Task(
                                        () =>
                                        {
                                            UploadResult result;
                                            try
                                            {
                                                result = _uploadFunction.Invoke(upload.request);
                                            }
                                            catch (Exception ex)
                                            {
                                                result = new();
                                                result.Result = UploadResult.ResultType.Failed;
                                                result.Message = "Exception during upload: " + ex.Message;
                                            }

                                            _readWriteLock.EnterWriteLock();
                                            try
                                            {
                                                //Update status with result
                                                var upload = _uploads[id];
                                                upload.status.Status = UploadStatus.StatusType.Completed;
                                                upload.status.Result = result;
                                                upload.status.StatusString = "Upload task" + id + " completed.";
                                                _uploads[id] = upload;

                                                _ongoingTasks.Remove(id);

                                                _logger.LogInformation("Task removed, total tasks running " + _ongoingTasks.Count);
                                            }
                                            finally
                                            {
                                                _readWriteLock.ExitWriteLock();
                                            } //Relase lock before entering beginNextTask to prevent deadlock

                                            TryBeginNextTask();

                                            _logger.LogInformation("Upload task for " + id + " finished, result: " + result.Result + " : " + result.Message );
                                        });

                        uploadTask.Start();
                        _ongoingTasks.Add(id, uploadTask);

                        upload.status.Status = UploadStatus.StatusType.Pending;
                        upload.status.StatusString = "Upload task" + id + " started.";
                        _uploads[id] = upload;

                        _logger.LogInformation("Task created, total tasks running " + _ongoingTasks.Count);
                    }
                    else
                    {
                        _logger.LogError("Upload request missing when adding queded task");
                    }
                }
            } finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

        public UploadIdentifier UploadFile(UploadRequest request)
        {
            _logger.LogInformation("UploadRequest for path: " + request.Path + " user: " + request.User + " recived");

            //TODO check if upload allready has been added
            var upload = new Upload();
            upload.status = new UploadStatus();
            upload.status.Status = UploadStatus.StatusType.NotStarted;
            upload.status.StatusString = "Upload " + request.Path + " for user " + request.User + " pending";
            upload.request = request;

            var id = Interlocked.Increment(ref _nextuploadId);
            _logger.LogInformation("Mapping uploadRequest " + request.Path + " to id " + id);

            _readWriteLock.EnterWriteLock();
            try
            {
                _uploads.Add(id, upload);
                _pendingUploads.Enqueue(id);
                _logger.LogInformation("Enque upload, Num pendingUploads: " + _pendingUploads.Count);
            }
            finally
            {
                _readWriteLock.ExitWriteLock();
            }//Relase lock before entering beginNextTask to prevent deadlock


            TryBeginNextTask();

            UploadIdentifier identifier = new();
            identifier.Identifier = ""+id;

            return identifier;
        }


        public UploadStatus? GetStatus(UInt64 uploadId)
        {
            _logger.LogDebug("GetStatus for id " + uploadId);

            _readWriteLock.EnterReadLock();
            try
            {
                Upload upload;
                if(_uploads.TryGetValue(uploadId, out upload))
                {
                    _logger.LogDebug("Return status " + upload.status);
                    return upload.status;
                }
                else
                {
                    _logger.LogInformation("Id not found, return null");
                    return null;
                }
            }
            finally
            {
                _readWriteLock.ExitReadLock();
            }
        }

        public void RemoveStatus(UInt64 uploadId)
        {
            _logger.LogInformation("RemoveStatus for id " + uploadId);

            _readWriteLock.EnterWriteLock();
            try {
                
                if (_ongoingTasks.ContainsKey(uploadId))
                {
                    _logger.LogWarning("Task is ongoing for id " + uploadId);
                }

                _uploads.Remove(uploadId);
            
            }   
            finally
            {
                _readWriteLock.ExitWriteLock();
            }
        }

    }
}
