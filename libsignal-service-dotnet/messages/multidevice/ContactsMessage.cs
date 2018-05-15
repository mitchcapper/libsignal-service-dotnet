﻿using libsignalservice.messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace libsignal.messages.multidevice
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class ContactsMessage
    {
        public SignalServiceAttachment Contacts { get; }
        public bool Complete { get; }
    }
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
}
