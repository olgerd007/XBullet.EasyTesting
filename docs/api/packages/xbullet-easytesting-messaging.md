---
uid: api-package-xbullet-easytesting-messaging
title: XBullet.EasyTesting.Messaging API
---

# XBullet.EasyTesting.Messaging

Transport-neutral message recording and assertions for destinations, headers, payloads, and ordering.

```xml
<PackageReference Include="XBullet.EasyTesting.Messaging" Version="VERSION" />
```

## Primary APIs

- <xref:XBullet.EasyTesting.Messaging.RecordedMessageBus> records published messages.
- <xref:XBullet.EasyTesting.Messaging.RecordedMessageBusAssertions> verifies total and route counts,
  predicates, absence, and exact ordered sequences.
- <xref:XBullet.EasyTesting.Messaging.RecordedMessageAssertions> verifies headers, structural payloads,
  and typed payload predicates.
- <xref:XBullet.EasyTesting.Messaging.RecordedMessage> represents one captured transport message.

See the [messaging guide](../../guides/messaging.md).
