# SimpleMessageBus
High load TCP message broker built on top of [Pipelines.IO](https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines).
This project was built in order to acquire knowledge about high performance in TCP world.
Message broker can handle ~1_000_000 of ack'ed simple messages per second (localhost).

![Alt text](rps.png?raw=true)

## To-do
- [x] Server messages ACK (acknowledgement) for multiple subscribers and resending undelivered messages.
- [x] N subscribers for one message class. 
- [x] Connect message where client send information about message classes ids binding.
- [ ] N handlers for one message class in one client.
- [ ] Basic tcp stack: ping/disconnect/reconnect features
- [ ] Client messages ACK and resending undelivered messages
- [ ] resizable server message buffers

## Problems
- Server messages buffers are of fixed length. Small buffer length can led to server to be stuck.
  In example, server has 1k buffer and client sends 1_005 messages. 5 messages cannot be send because server has no space for them,
  because, those 1k messages can be un-acked as ack message may be sent as 1_006 message.
  Solution1: resizable server buffers
  Solution2: drop those 5 messages. Client ack managements with resending those 5 messages.
- Unencapsulated tcp header/content. Delimiter was 2 bytes long earlier and this led to broken messages processing,
  because message/header contained those 2 bytes. Delimiter length was increased to 4 bytes and the problem disappeared(for now).

## Tcp protocol

| MessageType | MessageClassId   | MessageId    | TimerIndex      | Serialized message | Delimiter            |
|-------------|------------------|--------------|-----------------| -------------------| ---------------------|
| 1 byte      | 2 bytes (ushort) | 4 bytes(int) | 2 bytes(ushort) | ...                | 4 bytes <, \r, \n, > |

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

- try to unlock as fast as possible. Sometimes it's better to copy buffer under lock and use the copy for processing.
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
  The ACK firstly was built on bare message ids in bool array. 2 message classes and 3 sessions required 300kk loop ticks = about 6-7sec.
  Later I replaced message ids with a list of ranges 0,1,2,3 = (0..2), (2..4), and this improved performance a lot.
- Using a simple array with an index gives better performance than list, queue. But requires blocking logic.
  Use SpinUntil in bottleneck places (but the Func as an argument will give a load on GC so maybe spinOnce with while will be better)
- Do not allocate unnecessary memory. Use pre-allocated buffers. For example, create a buffer of nodes and allocate those nodes in ctor.
  Then in add/remove methods insert an object into node instead of using new Node. Use circular buffers instead of resizable ones(resize in timers if needed).
- Message length in tcp protocol vs messages delimiter. Message length in protocol requires additional step - to get message length.
  Delimiter is much faster(especially with Pipilines.io) but there is problem when message can contain delimiter bytes.
  For now delimiter is 4 bytes long
