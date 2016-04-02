using System;
using System.IO;
using WebDAVClientPortable;

namespace WebDAV.Test
{
    internal static class WebDAVClientExtensions
    {
        public static void UploadAsync(this WebDAVClient client, string sourceFileName, string destinationUri, Action<bool, int> callback)
        {
            if (string.IsNullOrEmpty(sourceFileName))
                throw new ArgumentNullException("sourceFileName");

            client.UploadAsync(() => new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read), true, destinationUri, callback);
        }

        public static bool Upload(this WebDAVClient client, string sourceFileName, string destinationUri)
        {
            if (string.IsNullOrEmpty(sourceFileName))
                throw new ArgumentNullException("sourceFileName");

            return client.Upload(() => new FileStream(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read), true, destinationUri);
        }

        public static void DownloadAsync(this WebDAVClient client, string fileUri, string outputFileName, Action<bool, int> callback)
        {
            if (string.IsNullOrEmpty(outputFileName))
                throw new ArgumentNullException("outputFileName");

            client.DownloadAsync(fileUri, () => new FileStream(outputFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None), true, callback);
        }

        public static bool Download(this WebDAVClient client, string fileUri, string outputFileName)
        {
            if (string.IsNullOrEmpty(outputFileName))
                throw new ArgumentNullException("outputFileName");

            return client.Download(fileUri, () => new FileStream(outputFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None), true);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // assumes WebDAV is enabled on local IIS

            var c = new WebDAVClient("http://localhost", null, "/test");
            c.UseDefaultCredentials = true;

            foreach (var item in c.List("/"))
                Console.WriteLine(item);

            Console.WriteLine();

            Console.WriteLine("Upload: {0}\n", c.Upload(@"D:\Test.pdf", "/test.pdf"));

            foreach (var item in c.List("/"))
                Console.WriteLine(item);

            Console.WriteLine();

            Console.WriteLine("Download: {0}\n", c.Download("test.tif", @"D:\Test.tif") && File.Exists(@"D:\Test.tif"));

            Console.WriteLine("CreateDirectory: {0}\n", c.CreateDirectory("NewFolder"));

            Console.WriteLine("Upload: {0}\n", c.Upload(@"D:\Test123.pdf", "NewFolder/123.pdf"));

            foreach (var item in c.List("/"))
                Console.WriteLine(item);

            Console.WriteLine();

            Console.WriteLine("Exists: {0}\n", c.FileExists("test.pdf"));

            Console.WriteLine("Delete: {0}\n", c.Delete("test.pdf"));

            Console.WriteLine("Delete: {0}\n", c.Delete("NewFolder"));

            foreach (var item in c.List("/"))
                Console.WriteLine(item);

            File.Delete(@"D:\Test.tif");

            //c.ListAsync("/", (bool success, int statusCode, IEnumerable<string> files) =>
            //{
            //    Console.WriteLine("List - Status Code: {0}\n", statusCode);

            //    foreach (var item in files)
            //        Console.WriteLine(item);

            //    Console.WriteLine();
            //});

            //c.UploadAsync(@"d:\test.pdf", "/test2.pdf", (bool success, int statusCode) =>
            //{
            //    Console.WriteLine("Upload - Status Code: {0}\n", statusCode);
            //});

            //c.DownloadAsync("test.tif", @"d:\test2.tif", (bool success, int statusCode) =>
            //{
            //    Console.WriteLine("Download - Status Code: {0}\n", statusCode);
            //});

            //c.CreateDirectoryAsync("hello", (bool success, int statusCode) =>
            //{
            //    Console.WriteLine("CreateDirectory - Status Code: {0}\n", statusCode);
            //});


            Console.ReadKey(true);
        }

    }
}
