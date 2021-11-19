using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SimpleMessageBus.Abstractions;
using SimpleMessageBus.Buffers;

namespace SimpleMessageBus.Client
{
    public class MessageAcknowledgementManager
    {
        private readonly ClientMessageManager _clientMessageManager;

        private readonly MessageAcknowledgementBuffer _buffer = new(2_000_000);

        // messageClassId => timerIndex => buffer
        private readonly Dictionary<ushort, Dictionary<ushort, List<AckRangeNode>>> _ackRanges = new();

        public MessageAcknowledgementManager(ClientMessageManager clientMessageManager)
        {
            _clientMessageManager = clientMessageManager;
        }

        public void AddMessageClassId(ushort messageClassId)
        {
            _ackRanges.Add(messageClassId, new Dictionary<ushort, List<AckRangeNode>>());
        }

        public void AcknowledgeMessage(ushort messageClassId, int messageId, ushort timerIndex)
        {
            _buffer.Add(messageClassId, messageId, timerIndex);
        }

        public async Task Start()
        {
            try
            {
                while (true)
                {
                    var messageIds = _buffer.TakeMax();

                    // combine message ids into ranges
                    // 0,1,2,3 = 0..4
                    // this way we spend only 1-2 ack messages per second
                    // TODO, move out
                    for (var i = 0; i < messageIds.Length; i++)
                    {
                        var message = messageIds.Span[i];

                        if (!_ackRanges[message.MessageClassId].ContainsKey(message.TimerIndex))
                            _ackRanges[message.MessageClassId].Add(message.TimerIndex, new List<AckRangeNode>());

                        var ackNodes = _ackRanges[message.MessageClassId][message.TimerIndex];

                        if (ackNodes.Count == 0 || ackNodes.Last().Last != message.MessageId)
                        {
                            ackNodes.Add(new AckRangeNode
                            {
                                First = message.MessageId,
                                Last = message.MessageId + 1
                            });

                            continue;
                        }

                        var lastNode = ackNodes.Last();

                        lastNode.Last++;
                    }

                    foreach (var (messageClassId, timerTicks) in _ackRanges)
                    foreach (var (timerIndex, nodes) in timerTicks)
                    {
                        foreach (var ackRangeNode in nodes)
                        {
                            _clientMessageManager.AddRawMessage(
                                MessageType.Ack,
                                $"{ackRangeNode.First}-{ackRangeNode.Last}",
                                messageClassId,
                                0,
                                timerIndex
                            );
                        }

                        nodes.Clear();
                    }

                    _buffer.AckAmount(messageIds.Length);
                    await Task.Delay(1000);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}