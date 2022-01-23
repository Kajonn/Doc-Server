namespace Doc_Server
{
    public struct UploadStatus
    {
        public enum StatusType
        {
            NotStarted,
            Pending,
            Completed
        }

        public UploadResult Result { get; set; }
        public StatusType Status { get; set; }
        public String StatusString { get; set; }
    }
}
