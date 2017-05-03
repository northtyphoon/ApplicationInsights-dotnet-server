﻿#if NET45
namespace Microsoft.ApplicationInsights.DependencyCollector.Implementation
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.DependencyCollector.Implementation.Operation;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Web.Implementation;

    /// <summary>
    /// Concrete class with all processing logic to generate RDD data from the callbacks received from HttpDesktopDiagnosticSourceListener.
    /// </summary>
    internal sealed class DesktopDiagnosticSourceHttpProcessing : HttpProcessing
    {
        private readonly CacheBasedOperationHolder telemetryTable;

        internal DesktopDiagnosticSourceHttpProcessing(TelemetryConfiguration configuration, CacheBasedOperationHolder telemetryTupleHolder, bool setCorrelationHeaders, ICollection<string> correlationDomainExclusionList, string appIdEndpoint)
            : base(configuration, SdkVersionUtils.GetSdkVersion("rdd" + RddSource.DiagnosticSourceDesktop + ":"), null, setCorrelationHeaders, correlationDomainExclusionList, appIdEndpoint)
        {
            if (telemetryTupleHolder == null)
            {
                throw new ArgumentNullException("telemetryTupleHolder");
            }

            this.telemetryTable = telemetryTupleHolder;
        }

        /// <summary>
        /// On request send callback from Http diagnostic source.
        /// </summary>
        /// <param name="request">The WebRequest object.</param>
        public void OnRequestSend(WebRequest request)
        {
            this.OnBegin(request, true);
        }

        /// <summary>
        /// On request send callback from Http diagnostic source.
        /// </summary>
        /// <param name="request">The WebRequest object.</param>
        /// <param name="response">The WebResponse object.</param>
        public void OnResponseReceive(WebRequest request, HttpWebResponse response)
        {
            this.OnEnd(null, request, response);
        }

        /// <summary>
        /// Implemented by the derived class for adding the tuple to its specific cache.
        /// </summary>
        /// <param name="webRequest">The request which acts the key.</param>
        /// <param name="telemetry">The dependency telemetry for the tuple.</param>
        /// <param name="isCustomCreated">Boolean value that tells if the current telemetry item is being added by the customer or not.</param>
        protected override void AddTupleForWebDependencies(WebRequest webRequest, DependencyTelemetry telemetry, bool isCustomCreated)
        {
            var telemetryTuple = new Tuple<DependencyTelemetry, bool>(telemetry, isCustomCreated);
            this.telemetryTable.Store(ClientServerDependencyTracker.GetIdForRequestObject(webRequest), telemetryTuple);
        }

        /// <summary>
        /// Implemented by the derived class for getting the tuple from its specific cache.
        /// </summary>
        /// <param name="webRequest">The request which acts as the key.</param>
        /// <returns>The tuple for the given request.</returns>
        protected override Tuple<DependencyTelemetry, bool> GetTupleForWebDependencies(WebRequest webRequest)
        {
            return this.telemetryTable.Get(ClientServerDependencyTracker.GetIdForRequestObject(webRequest));
        }

        /// <summary>
        /// Implemented by the derived class for removing the tuple from its specific cache.
        /// </summary>
        /// <param name="webRequest">The request which acts as the key.</param>
        protected override void RemoveTupleForWebDependencies(WebRequest webRequest)
        {
            this.telemetryTable.Remove(ClientServerDependencyTracker.GetIdForRequestObject(webRequest));
        }
    }
}
#endif