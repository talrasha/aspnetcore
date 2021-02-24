// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure;
using Microsoft.AspNetCore.Testing;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Server.Kestrel.Core.Tests
{
    public class Http3TimeoutTests : Http3TestBase
    {
        [Fact]
        public async Task HEADERS_IncompleteFrameReceivedWithinRequestHeadersTimeout_AbortsConnection()
        {
            var timerStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _mockTimeoutControl.Setup(tc => tc.SetTimeout(It.IsAny<long>(), TimeoutReason.RequestHeaders))
                .Callback((long ticks, TimeoutReason reason) =>
                {
                    _timeoutControl.SetTimeout(ticks, reason);
                    timerStartedTcs.SetResult();
                });
            var mockSystemClock = _serviceContext.MockSystemClock;
            var limits = _serviceContext.ServerOptions.Limits;

            _timeoutControl.Initialize(mockSystemClock.UtcNow.Ticks);

            var requestStream = await InitializeConnectionAndStreamsAsync(_noopApplication).DefaultTimeout();

            var controlStream = await GetInboundControlStream().DefaultTimeout();
            await controlStream.ExpectSettingsAsync().DefaultTimeout();

            await timerStartedTcs.Task.DefaultTimeout();

            await requestStream.SendHeadersPartialAsync().DefaultTimeout();

            AdvanceClock(limits.RequestHeadersTimeout + Heartbeat.Interval);

            _mockTimeoutHandler.Verify(h => h.OnTimeout(It.IsAny<TimeoutReason>()), Times.Never);

            AdvanceClock(TimeSpan.FromTicks(1));

            _mockTimeoutHandler.Verify(h => h.OnTimeout(TimeoutReason.RequestHeaders), Times.Once);

            await WaitForConnectionErrorAsync<Microsoft.AspNetCore.Http.BadHttpRequestException>(
                ignoreNonGoAwayFrames: false,
                expectedLastStreamId: 0,
                expectedErrorCode: Http3ErrorCode.RequestRejected,
                expectedErrorMessage: CoreStrings.BadRequest_RequestHeadersTimeout).DefaultTimeout();

            _mockTimeoutHandler.VerifyNoOtherCalls();
        }
    }
}
