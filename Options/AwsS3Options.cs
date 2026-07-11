// Options/AwsS3Options.cs
namespace AttendancePipeline
{
    public class AwsS3Options
    {
        public string Region { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string ServiceURL { get; set; } = string.Empty;
    }
}