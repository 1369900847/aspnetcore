// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Quic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Quic.Internal
{
    internal class QuicConnectionContext : TransportMultiplexedConnection
    {
        private QuicConnection _connection;
        private readonly QuicTransportContext _context;
        private readonly IQuicTrace _log;

        private ValueTask _closeTask;

        public QuicConnectionContext(QuicConnection connection, QuicTransportContext context)
        {
            _log = context.Log;
            _context = context;
            _connection = connection;
            Features.Set<ITlsConnectionFeature>(new FakeTlsConnectionFeature());

            _log.NewConnection(ConnectionId);
        }

        public ValueTask<StreamContext> StartUnidirectionalStreamAsync()
        {
            var stream = _connection.OpenUnidirectionalStream();
            return new ValueTask<StreamContext>(new QuicStreamContext(stream, this, _context));
        }

        public ValueTask<StreamContext> StartBidirectionalStreamAsync()
        {
            var stream = _connection.OpenBidirectionalStream();
            return new ValueTask<StreamContext>(new QuicStreamContext(stream, this, _context));
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (_closeTask != default)
                {
                    _closeTask  = _connection.CloseAsync(errorCode: 0);
                    await _closeTask;
                }
                else 
                {
                    await _closeTask;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to gracefully shutdown connection.");
            }

            _connection.Dispose();
        }

        public override void Abort(ConnectionAbortedException abortReason)
        {
            _closeTask = _connection.CloseAsync(errorCode: _context.Options.AbortErrorCode);
        }

        public override async ValueTask<StreamContext> AcceptAsync(CancellationToken cancellationToken = default)
        {
            var stream = await _connection.AcceptStreamAsync(cancellationToken);
            try
            {
                _ = stream.CanRead;
            }
            catch (Exception)
            {
                return null;
            }
            return new QuicStreamContext(stream, this, _context);
        }

        public override ValueTask<StreamContext> ConnectAsync(IFeatureCollection features = null, CancellationToken cancellationToken = default)
        {
            QuicStream quicStream;

            if (features != null)
            {
                var streamDirectionFeature = features.Get<IStreamDirectionFeature>();
                if (streamDirectionFeature.CanRead)
                {
                    quicStream = _connection.OpenBidirectionalStream();
                }
                else
                {
                    quicStream = _connection.OpenUnidirectionalStream();
                }
            }
            else
            {
                quicStream = _connection.OpenBidirectionalStream();
            }

            return new ValueTask<StreamContext>(new QuicStreamContext(quicStream, this, _context));
        }
    }
}
