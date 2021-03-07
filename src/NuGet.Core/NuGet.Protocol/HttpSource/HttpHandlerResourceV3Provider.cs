// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using System.Runtime.InteropServices;

namespace NuGet.Protocol
{
    public class HttpHandlerResourceV3Provider : ResourceProvider
    {
        public HttpHandlerResourceV3Provider()
            : base(typeof(HttpHandlerResource),
                  nameof(HttpHandlerResourceV3Provider),
                  NuGetResourceProviderPositions.Last)
        {
        }

        public override Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Debug.Assert(source.PackageSource.IsHttp, "HTTP handler requested for a non-http source.");

            HttpHandlerResourceV3 curResource = null;

            if (source.PackageSource.IsHttp)
            {
                curResource = CreateResource(source.PackageSource);
            }

            return Task.FromResult(new Tuple<bool, INuGetResource>(curResource != null, curResource));
        }

        private static HttpHandlerResourceV3 CreateResource(PackageSource packageSource)
        {
            var sourceUri = packageSource.SourceUri;
            var proxy = ProxyCache.Instance.GetProxy(sourceUri);

            HttpClientHandler clientHandler;

            // replace the handler with the proxy aware handler if not in browser
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Create("browser")))
            {
                clientHandler = new HttpClientHandler
                {
                    Proxy = proxy,
                    AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
                };
            }
            else
            {
                clientHandler = new HttpClientHandler();
            }

            // Setup http client handler client certificates
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Create("browser")) && packageSource.ClientCertificates != null)
            {
                clientHandler.ClientCertificates.AddRange(packageSource.ClientCertificates.ToArray());
            }

            // HTTP handler pipeline can be injected here, around the client handler
            HttpMessageHandler messageHandler = new ServerWarningLogHandler(clientHandler);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Create("browser")) && proxy != null)
            {
                messageHandler = new ProxyAuthenticationHandler(clientHandler, HttpHandlerResourceV3.CredentialService?.Value, ProxyCache.Instance);
            }

#if !IS_CORECLR
            {
                var innerHandler = messageHandler;

                messageHandler = new StsAuthenticationHandler(packageSource, TokenStore.Instance)
                {
                    InnerHandler = messageHandler
                };
            }
#endif
            if(!RuntimeInformation.IsOSPlatform(OSPlatform.Create("browser")))
            {
                var innerHandler = messageHandler;

                messageHandler = new HttpSourceAuthenticationHandler(packageSource, clientHandler, HttpHandlerResourceV3.CredentialService?.Value)
                {
                    InnerHandler = innerHandler
                };
            }

            var resource = new HttpHandlerResourceV3(clientHandler, messageHandler);

            return resource;
        }
    }
}
