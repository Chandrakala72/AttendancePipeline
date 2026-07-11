// UploadToS3.cs
//
// Uploads a locally-stored attendance report file to S3, under a
// date-partitioned key: attendance/raw/date=YYYY-MM-DD/<filename>
//
// NuGet packages required:
//   dotnet add package AWSSDK.S3
//   dotnet add package Microsoft.Extensions.Configuration.Json
//
// Configuration required (appsettings.json):
//   AWS:Region, AWS:BucketName, AWS:ServiceURL (optional)
//   AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY still come from environment
//   variables or an EC2/ECS instance role, same as before.
//
// Standalone usage:
//   dotnet run -- ./transaction_data.xlsx {today's date - 1}
//
// Usage from your automation code:
//   var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
//   var uploader = new S3ReportUploader(config);
//   var key = await uploader.UploadReportToS3Async("/path/to/report.xlsx", "2026-07-03");

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AttendancePipeline
{
    public class S3ReportUploader
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        /// <summary>
        /// Production constructor — builds a real S3 client from appsettings.json.
        /// AWS:ServiceURL (optional) lets you point at LocalStack/MinIO for local testing
        /// instead of real AWS, without touching any of the upload logic below.
        /// </summary>
        public S3ReportUploader(IConfiguration configuration)
        {
            var awsSection = configuration.GetSection("AWS");
            var region = awsSection["Region"] ?? string.Empty;
            _bucketName = awsSection["BucketName"] ?? string.Empty;
            var accessKey = awsSection["AccessKey"];
            var secretKey = awsSection["SecretKey"];
            //var endpointOverride = awsSection["ServiceURL"] ?? string.Empty;

            var credentials = new BasicAWSCredentials(accessKey, secretKey);

            _s3Client = new AmazonS3Client(
                            credentials,
                            RegionEndpoint.GetBySystemName(region));
        }

        /// <summary>
        /// Test constructor — inject any IAmazonS3 (real, LocalStack-pointed, or a mock).
        /// Used by unit tests; not used in normal production startup.
        /// </summary>
        public S3ReportUploader(IAmazonS3 s3Client, string bucketName)
        {
            _s3Client = s3Client;
            _bucketName = bucketName;
        }

        /// <summary>
        /// Uploads a single local file to S3 under the attendance/raw/ prefix,
        /// partitioned by date. Retries transient failures with exponential backoff.
        /// </summary>
        /// <param name="localFilePath">Path to the file on the local server.</param>
        /// <param name="date">Date string in YYYY-MM-DD format, used for partitioning.</param>
        /// <param name="retries">Number of retry attempts on transient failure.</param>
        /// <returns>The S3 object key that was uploaded to.</returns>
        public async Task<string> UploadReportToS3Async(string localFilePath, string date, int retries = 3)
        {
            if (!File.Exists(localFilePath))
            {
                throw new FileNotFoundException($"Local file not found: {localFilePath}");
            }

            if (!Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
            {
                throw new ArgumentException($"Invalid date format \"{date}\", expected YYYY-MM-DD");
            }

            var fileName = Path.GetFileName(localFilePath);
            var key = $"AttendanceReports/date={date}/{fileName}";

            Exception? lastError = null;

            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    using var fileStream = File.OpenRead(localFilePath);

                    var request = new PutObjectRequest
                    {
                        BucketName = _bucketName,
                        Key = key,
                        InputStream = fileStream,
                        ContentType = GetContentType(fileName),
                        AutoCloseStream = true,
                    };

                    await _s3Client.PutObjectAsync(request);

                    Console.WriteLine($"[UploadToS3] SUCCESS: s3://{_bucketName}/{key}");
                    return key;
                }
                catch (AmazonS3Exception ex) when (IsNonRetryable(ex))
                {
                    // Don't retry on permission/config errors — they won't fix themselves
                    Console.Error.WriteLine($"[UploadToS3] Non-retryable error for {key}: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Console.Error.WriteLine($"[UploadToS3] Attempt {attempt + 1} failed for {key}: {ex.Message}");

                    if (attempt < retries)
                    {
                        var delayMs = 500 * (int)Math.Pow(2, attempt); // 500ms, 1s, 2s...
                        await Task.Delay(delayMs);
                    }
                }
            }

            throw new Exception(
                $"[UploadToS3] All {retries + 1} attempts failed for {localFilePath}: {lastError?.Message}");
        }

        private static bool IsNonRetryable(AmazonS3Exception ex)
        {
            return ex.ErrorCode == "AccessDenied" || ex.ErrorCode == "NoSuchBucket";
        }

        private static string GetContentType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xls" => "application/vnd.ms-excel",
                ".csv" => "text/csv",
                ".json" => "application/json",
                _ => "application/octet-stream",
            };
        }
    }

    // Allows standalone CLI usage: dotnet run -- <filePath> <date>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: dotnet run -- <localFilePath> <YYYY-MM-DD>");
                return 1;
            }

            var filePath = args[0];
            var date = args[1];

            try
            {
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile(Path.Combine("Settings", "appsettings.json"), optional: false, reloadOnChange: false)
                    .Build();

                var uploader = new S3ReportUploader(configuration);
                var key = await uploader.UploadReportToS3Async(filePath, date);
                Console.WriteLine($"Done. Uploaded to key: {key}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Upload failed: {ex.Message}");
                return 1;
            }
        }
    }
}