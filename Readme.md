# SimpleMessageBus
High load TCP message broker built on top of [Pipelines.IO](https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines).
This project was built in order to acquire knowledge about high performance in TCP world.
Message broker can handle 900k+ ack'ed simple messages per second (localhost).

## To-do
- [x] Messages ACK (acknowledgement) and resending undelivered messages.
- [ ] Basic tcp stack: ping/disconnect/reconnect features
- [ ] N subscribers for one message class. N handlers for one message class.

## Tcp protocol

| MessageType | MessageClassId   | MessageId    | TimerIndex      |
|-------------|------------------|--------------|-----------------|
| 1 byte      | 2 bytes (ushort) | 4 bytes(int) | 2 bytes(ushort) |

1. MessageType:
```
Heartbeat = '0',
Message = '1',
Subscribe = '2',
Ack = '3',
Connect = '4'
```
2. MessageClassId: unique id of message class(ShopCreatedEvent is 1 in example). 
Used for binding types to buffers for O(1) access. Binding dictionary should be send by Connect message.
4. MessageId: after message was sent to server the buffer will inject messageId(buffer index) into the message. This id is used for ACK
5. TimerIndex: there is timer in the server to find undelivered messages. TimerIndex is counter in that timer. Moved to protocol to achieve O(1) access to ACK buffers.

## What I learnt from this project/challenges i met
My C# learning path started from this project(i know, it's crazy). I spent about few full-day working months on it.

- try to unlock as fast as possible. Sometimes it's better to copy buffer under lock and use the copy for processing
then process buffer under lock.
- divide work on workers(this let me achieve 2kk serialization rps from about 800k-1kk rps on single worker).
This requires ordering feature and i came up with an idea of reserving index for the result under shared lock.
No matter how long will take the serialization process all messages chunks will be ordered properly.
The flow looks like this:
```
resultWorker waits for index 0 to be set
worker1 reserved index 0
worker2 reserved index 1
worker2 set result to 1
worker3 reserved index 2
worker3 set result to 2
worker1 set result to 0
resultWorker consumes 0-2 result indexes
resultWorker waits for index 3 to be set
...
```
- perform only simple and fast operations under locks. Move complex logic to timers.
  Messages ack was built this way. When a message is consumed by the handler we add its id to a circular buffer.
  Once a second we take all consumed ids and transform them into ranges 0,1,2,4,5 = (0..3), (4..6)
- adjust your algorithms for O(1) as much as possible and try to avoid a big amount of loops.
  The ACK firstly was built on bare message ids. MessageClassId => sessionId => timerIndex = > 50kk bool buffer
  that shows whether the index is acked or not. 2 message classes and 3 sessions required 300kk loop ticks = about 6-7sec.
  Later I replaced message ids with a list of ranges 0,1,2,3 = (0..2), (2..4), and this improved performance a lot.
- Using a simple array with an index gives better performance than list, queue. But requires blocking logic.
  Use SpinUntil in bottleneck places (but the Func as an argument will give a load on GC so maybe spinOnce with while will be better)
- Do not allocate unnecessary memory. Use pre-allocated buffers. For example, create a buffer of nodes and allocate those nodes in ctor.
  Then in add/remove just insert an object into node instead of using new Node. Use circular buffers instead of resizable ones(resize in timers if needed).
