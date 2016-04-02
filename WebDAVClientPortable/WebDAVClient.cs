using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace WebDAVClientPortable
{
    public sealed class WebDAVClient
    {
        // http://webdav.org/specs/rfc4918.html

        private const char DirectorySeparatorChar = '/';
        private readonly string _server;
        private readonly string _basePath;

        public string Server
        {
            get { return _server; }
        }

        public string BasePath
        {
            get { return _basePath; }
        }

        public ICredentials Credentials { get; set; }

        public bool UseDefaultCredentials { get; set; }

        public WebDAVClient(string server, int? port, string basePath)
        {
            if (string.IsNullOrEmpty(server))
                throw new ArgumentNullException("server");

            _server = server.TrimEnd(DirectorySeparatorChar);

            if (string.IsNullOrEmpty(basePath))
                _basePath = DirectorySeparatorChar.ToString();
            else
                _basePath = string.Format("{1}{0}{1}", basePath.Trim(DirectorySeparatorChar), DirectorySeparatorChar);

            this.UseDefaultCredentials = true;
        }

        private Uri GetAbsoluteUri(string relativePath, bool appendTrailingSlash)
        {
            string completePath = _basePath;
            if (!string.IsNullOrEmpty(relativePath))
                completePath += relativePath.Trim(DirectorySeparatorChar);

            if (appendTrailingSlash)
                completePath += DirectorySeparatorChar;

            return new Uri(string.Format("{0}{1}", _server, completePath));
        }

        private HttpWebRequest CreateHttpWebRequest(Uri uri, string method)
        {
            var request = (HttpWebRequest)HttpWebRequest.Create(uri);
            request.Method = method;

            request.UseDefaultCredentials = this.UseDefaultCredentials;

            if (!this.UseDefaultCredentials)
                request.Credentials = this.Credentials;

            //request.PreAuthenticate = true;

            // The following line fixes an authentication problem explained here:
            // http://www.devnewsgroups.net/dotnetframework/t9525-http-protocol-violation-long.aspx
            //  System.Net.ServicePointManager.Expect100Continue = false;
            // or
            //  request.ServicePoint.Expect100Continue = false;

            return request;
        }


        public void ListAsync(string directoryPath, Action<bool, int, IEnumerable<string>> callback, bool returnRelativePaths = false)
        {
            var listUri = GetAbsoluteUri(directoryPath, true);

            // var propfind = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><propfind xmlns=\"DAV:\"><propname/></propfind>";
            var propfind = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><propfind xmlns=\"DAV:\"><prop><resourcetype /></prop></propfind>";
            var requestContent = Encoding.UTF8.GetBytes(propfind);

            var request = CreateHttpWebRequest(listUri, "PROPFIND");

            request.Headers["Depth"] = "1";

            request.ContentType = "text/xml";

            AsyncCallback responseCallback = (IAsyncResult responseAsyncResult) =>
            {
                int statusCode = 0;
                bool success = false;
                var files = new List<string>();

                using (var response = (HttpWebResponse)request.EndGetResponse(responseAsyncResult))
                {
                    statusCode = (int)response.StatusCode;
                    success = (statusCode == 207);

                    if (response.ContentLength > 0)
                    {
                        using (var stream = response.GetResponseStream())
                        {
                            var xDoc = XDocument.Load(stream);

                            foreach (var href in xDoc.Descendants(XName.Get("{DAV:}href")))
                            {
                                var filePath = Uri.UnescapeDataString(href.Value);

                                if (returnRelativePaths)
                                {
                                    var fileParts = filePath.Split(new string[] { _basePath }, StringSplitOptions.RemoveEmptyEntries);
                                    if (fileParts.Length > 0)
                                    {
                                        filePath = fileParts[fileParts.Length - 1];
                                        if (filePath == directoryPath || filePath == _server)
                                            continue;
                                    }
                                }

                                files.Add(filePath);
                            }
                        }
                    }
                }

                if (callback != null)
                    callback(success, statusCode, files);
            };

            request.BeginGetRequestStream(new AsyncCallback((IAsyncResult requestAsyncResult) =>
            {
                using (var requestStream = request.EndGetRequestStream(requestAsyncResult))
                {
                    requestStream.Write(requestContent, 0, requestContent.Length);
                }

                request.BeginGetResponse(responseCallback, null);

            }), null);
        }

        public IEnumerable<string> List(string directoryPath, bool returnRelativePaths = false)
        {
            IEnumerable<string> fileList = null;
            using (var waitHandle = new ManualResetEvent(false))
            {
                ListAsync(directoryPath, (bool success, int statusCode, IEnumerable<string> files) =>
                {
                    if (success)
                        fileList = files;

                    waitHandle.Set();
                },
                returnRelativePaths);

                waitHandle.WaitOne();
            }
            return fileList;
        }


        public void UploadAsync(Stream sourceStream, string destinationUri, Action<bool, int> callback)
        {
            if (sourceStream == null)
                throw new ArgumentNullException("sourceStream");

            UploadAsync(() => sourceStream, false, destinationUri, callback);
        }

        public bool Upload(Stream sourceStream, string destinationUri)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                UploadAsync(sourceStream, destinationUri, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }

        public void UploadAsync(Func<Stream> getInputStream, bool closeInputStream, string destinationUri, Action<bool, int> callback)
        {
            if (getInputStream == null)
                throw new ArgumentNullException("getInputStream");

            if (string.IsNullOrEmpty(destinationUri))
                throw new ArgumentNullException("destinationUri");

            var absoluteDestUri = GetAbsoluteUri(destinationUri, false);
            var request = CreateHttpWebRequest(absoluteDestUri, "PUT");

            request.BeginGetRequestStream(new AsyncCallback((IAsyncResult requestAsyncResult) =>
            {
                using (var requestStream = request.EndGetRequestStream(requestAsyncResult))
                {
                    if (closeInputStream)
                    {
                        using (var sourceStream = getInputStream())
                        {
                            sourceStream.CopyTo(requestStream);
                        }
                    }
                    else
                    {
                        getInputStream().CopyTo(requestStream);
                    }
                }

                request.BeginGetResponse(new AsyncCallback((IAsyncResult responseAsyncResult) =>
                {
                    int statusCode = 0;
                    bool success = false;
                    using (var response = (HttpWebResponse)request.EndGetResponse(responseAsyncResult))
                    {
                        statusCode = (int)response.StatusCode;
                        success = (statusCode == 200 || statusCode == 201);
                    }

                    if (callback != null)
                        callback(success, statusCode);
                }), null);

            }), null);
        }

        public bool Upload(Func<Stream> getInputStream, bool closeInputStream, string destinationUri)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                UploadAsync(getInputStream, closeInputStream, destinationUri, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }


        public void DownloadAsync(string fileUri, Stream outputStream, Action<bool, int> callback)
        {
            if (outputStream == null)
                throw new ArgumentNullException("outputStream");

            DownloadAsync(fileUri, () => outputStream, false, callback);
        }

        public bool Download(string fileUri, Stream outputStream)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                DownloadAsync(fileUri, outputStream, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }

        public void DownloadAsync(string fileUri, Func<Stream> getOutputStream, bool closeOutputStream, Action<bool, int> callback)
        {
            if (string.IsNullOrEmpty(fileUri))
                throw new ArgumentNullException("fileUri");

            if (getOutputStream == null)
                throw new ArgumentNullException("getOutputStream");

            var absoluteFileUri = GetAbsoluteUri(fileUri, false);
            var request = CreateHttpWebRequest(absoluteFileUri, "GET");

            request.BeginGetResponse(new AsyncCallback((IAsyncResult responseAsyncResult) =>
            {
                int statusCode = 0;
                bool success = false;
                using (var response = (HttpWebResponse)request.EndGetResponse(responseAsyncResult))
                {
                    statusCode = (int)response.StatusCode;
                    success = (statusCode == 200);

                    using (var responseStream = response.GetResponseStream())
                    {
                        if (closeOutputStream)
                        {
                            using (var outputStream = getOutputStream())
                            {
                                responseStream.CopyTo(outputStream);
                            }
                        }
                        else
                        {
                            responseStream.CopyTo(getOutputStream());
                        }
                    }
                }

                if (callback != null)
                    callback(success, statusCode);
            }), null);
        }

        public bool Download(string fileUri, Func<Stream> getOutputStream, bool closeOutputStream)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                DownloadAsync(fileUri, getOutputStream, closeOutputStream, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }


        public void CreateDirectoryAsync(string directoryUri, Action<bool, int> callback)
        {
            if (string.IsNullOrEmpty(directoryUri))
                throw new ArgumentNullException("directoryUri");

            var absoluteDirUri = GetAbsoluteUri(directoryUri, false);
            var request = CreateHttpWebRequest(absoluteDirUri, "MKCOL");

            request.BeginGetResponse(new AsyncCallback((IAsyncResult responseAsyncResult) =>
            {
                int statusCode = 0;
                bool success = false;
                using (var response = (HttpWebResponse)request.EndGetResponse(responseAsyncResult))
                {
                    statusCode = (int)response.StatusCode;
                    success = (statusCode == 200 || statusCode == 201);
                }

                if (callback != null)
                    callback(success, statusCode);
            }), null);
        }

        public bool CreateDirectory(string directoryUri)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                CreateDirectoryAsync(directoryUri, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }


        public void DeleteAsync(string fileOrDirectoryUri, Action<bool, int> callback)
        {
            if (string.IsNullOrEmpty(fileOrDirectoryUri))
                throw new ArgumentNullException("fileOrDirectoryUri");

            var absoluteUri = GetAbsoluteUri(fileOrDirectoryUri, fileOrDirectoryUri.EndsWith(DirectorySeparatorChar.ToString()));
            var request = CreateHttpWebRequest(absoluteUri, "DELETE");

            request.BeginGetResponse(new AsyncCallback((IAsyncResult responseAsyncResult) =>
            {
                int statusCode = 0;
                bool success = false;
                using (var response = (HttpWebResponse)request.EndGetResponse(responseAsyncResult))
                {
                    statusCode = (int)response.StatusCode;
                    success = (statusCode == 200 || statusCode == 204);
                }

                if (callback != null)
                    callback(success, statusCode);
            }), null);
        }

        public bool Delete(string fileOrDirectoryUri)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                DeleteAsync(fileOrDirectoryUri, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }


        public void FileExistsAsync(string fileUri, Action<bool, int> callback)
        {
            if (string.IsNullOrEmpty(fileUri))
                throw new ArgumentNullException("fileUri");

            var absoluteUri = GetAbsoluteUri(fileUri, false);
            var request = CreateHttpWebRequest(absoluteUri, "HEAD");

            request.BeginGetResponse(new AsyncCallback((IAsyncResult responseAsyncResult) =>
            {
                int statusCode = 0;
                bool success = false;

                try
                {
                    using (var response = (HttpWebResponse)request.EndGetResponse(responseAsyncResult))
                    {
                        statusCode = (int)response.StatusCode;
                        success = (statusCode == 200);
                    }
                }
                catch (WebException ex)
                {
                    success = (ex.Status == WebExceptionStatus.Success);
                }

                if (callback != null)
                    callback(success, statusCode);

            }), null);
        }

        public bool FileExists(string fileUri)
        {
            bool result = false;
            using (var waitHandle = new ManualResetEvent(false))
            {
                FileExistsAsync(fileUri, (bool success, int statusCode) =>
                {
                    result = success;
                    waitHandle.Set();
                });

                waitHandle.WaitOne();
            }
            return result;
        }

    }
}
