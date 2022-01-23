namespace Doc_Server
{
    public struct UploadRequest
    {
        public string Path { get; set; }
        public string User { get; set; }
    }
    public struct UploadStatus
    {
        public enum Status {
            Error,
            Pending,
            OnGoing,
            Completed
        }

        public Status status;
        public String statusString;
    }

    public struct UploadResult
    {
        public enum Status{
            Failed,
            Completed
        }
        public Status status;
        public String statusString;
    }

    public delegate UploadResult UploadDelegate(UploadRequest request);

    public class UploadManager
    {
        public struct Upload
        {
            public UploadStatus status;
            public UploadRequest request;
        }
        
        private ReaderWriterLockSlim readWriteLock = new ReaderWriterLockSlim();

        private Dictionary<UInt64, Upload> uploads = new Dictionary<ulong, Upload>();

        private Queue<UInt64> pendingUploads = new Queue<UInt64>();
        private Dictionary<UInt64, Task> ongoingTasks = new Dictionary<UInt64, Task>();

        private UInt64 nextuploadId = 0;

        private UploadDelegate uploadFunction;
        private int maxTasks;

        public UploadManager(UploadDelegate uploadDelegate, int maxTasks_)
        {
            this.uploadFunction = uploadDelegate;
            this.maxTasks = maxTasks_;
        }

        private void beginNextTask()
        {
            readWriteLock.EnterWriteLock();
            try { 
                if (pendingUploads.Count() > 0 && ongoingTasks.Count()<maxTasks)
                {
                    UInt64 id = pendingUploads.Dequeue();
                    Upload upload;
                    if (uploads.TryGetValue(id, out upload))
                    {
                        var uploadTask = new Task(
                                        () =>
                                        {
                                            UploadResult result;
                                            try
                                            {
                                                result = uploadFunction.Invoke(upload.request);
                                            }
                                            catch (Exception ex)
                                            {
                                                result = new UploadResult();
                                                result.status = UploadResult.Status.Failed;
                                                result.statusString = ex.Message;
                                            }

                                            readWriteLock.EnterWriteLock();
                                            try
                                            {
                                                var upload = uploads[id];
                                                upload.status.status =
                                                    result.status == UploadResult.Status.Completed ?
                                                    UploadStatus.Status.Completed : UploadStatus.Status.Error;
                                                upload.status.statusString = result.statusString;
                                                uploads.Add(id, upload);
                                            }
                                            finally
                                            {
                                                readWriteLock.ExitWriteLock();
                                            }

                                            lock (ongoingTasks)
                                            {
                                                ongoingTasks.Remove(id);
                                            }

                                            beginNextTask();
                                        });
                        uploadTask.Start();
                        ongoingTasks.Add(id, uploadTask);
                    } else
                    {
                        //TODO missing upload status
                    }
                }
            } finally
            {
                readWriteLock.ExitWriteLock();
            }
        }

        public UInt64 uploadFile(UploadRequest request)
        {
            var upload = new Upload();
            upload.status = new UploadStatus();
            upload.status.status = UploadStatus.Status.Pending;
            upload.status.statusString = "Upload " + request.Path + " for user " + request.User + " pending";
            upload.request = request;

            var id = Interlocked.Increment(ref nextuploadId);

            readWriteLock.EnterWriteLock();
            try
            {
                uploads.Add(id, upload);
            } finally
            {
                readWriteLock.ExitWriteLock();
            }

            beginNextTask();

            return id;
        }


        public UploadStatus? getStatus(UInt64 uploadId)
        {
            readWriteLock.EnterReadLock();
            try
            {
                Upload upload;
                if(uploads.TryGetValue(uploadId, out upload))
                {
                    return upload.status;
                }
                else
                {
                    return null;
                }
            }
            finally
            {
                readWriteLock.ExitReadLock();
            }
        }

        public void removeStatus(UInt64 uploadId)
        {
            readWriteLock.EnterWriteLock();
            try {
                if (ongoingTasks.ContainsKey(uploadId))
                {
                    //TODO warn that finish status will be removed
                }

                uploads.Remove(uploadId);
            
            }   
            finally
            {
                readWriteLock.ExitWriteLock();
            }
        }

    }
}
