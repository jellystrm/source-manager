using System.Collections.Concurrent;
using System.Threading.Channels;
using Jellyfin.Plugin.Jellystrm.Models;

namespace Jellyfin.Plugin.Jellystrm.Services;

public sealed class RequestEventBroker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<MediaRequestDto>>> _channels = new(StringComparer.OrdinalIgnoreCase);

    public ChannelReader<MediaRequestDto> Subscribe(string userId, out IDisposable subscription)
    {
        var channel = Channel.CreateUnbounded<MediaRequestDto>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var subscriptionId = Guid.NewGuid();
        var userChannels = _channels.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<MediaRequestDto>>());
        userChannels[subscriptionId] = channel;
        subscription = new Subscription(this, userId, subscriptionId);
        return channel.Reader;
    }

    public void Publish(string userId, MediaRequestDto request)
    {
        if (!_channels.TryGetValue(userId, out var userChannels))
        {
            return;
        }

        foreach (var channel in userChannels.Values)
        {
            _ = channel.Writer.TryWrite(request);
        }
    }

    private void Unsubscribe(string userId, Guid subscriptionId)
    {
        if (!_channels.TryGetValue(userId, out var userChannels))
        {
            return;
        }

        if (userChannels.TryRemove(subscriptionId, out var channel))
        {
            channel.Writer.TryComplete();
        }

        if (userChannels.IsEmpty)
        {
            _channels.TryRemove(userId, out _);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly RequestEventBroker _broker;
        private readonly string _userId;
        private readonly Guid _subscriptionId;
        private bool _disposed;

        public Subscription(RequestEventBroker broker, string userId, Guid subscriptionId)
        {
            _broker = broker;
            _userId = userId;
            _subscriptionId = subscriptionId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _broker.Unsubscribe(_userId, _subscriptionId);
        }
    }
}
