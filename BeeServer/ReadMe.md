﻿# Server

- Server 负责提供当前帧，并将所有客户端的输入进行汇总计算，得出一个稍慢于客户端的状态，在有更改的时候主动给客户端进行广播



- 初始化
  简单的让客户端发送一条初始化消息，服务端给予他一个当前的状态（或者N帧之前的？然后加上N之后的动作序列）和当前帧
  客户端拿到后修改自己本地的帧