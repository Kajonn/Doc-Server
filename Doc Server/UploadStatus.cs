namespace Doc_Server
{
    public struct UploadStatus
    {
        public enum StatusType
        {
            Error,
            Pending,
            OnGoing,
            Completed
        }

        public StatusType Status { get; set; }
        public String StatusString { get; set; }
    }
}
