namespace Doc_Server
{
    public struct UploadResult
    {
        public enum ResultType
        {
            Failed,
            Completed
        }
        public ResultType Result { get; set; }
        public String Message { get; set; }
    }
}
