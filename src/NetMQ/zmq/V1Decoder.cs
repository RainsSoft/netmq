namespace NetMQ.zmq
{
	public class V1Decoder : DecoderBase
	{    
		private const int OneByteSizeReadyState = 0;
		private const int EightByteSizeReadyState = 1;
		private const int FlagsReadyState = 2;
		private const int MessageReadyState = 3;

		private readonly ByteArraySegment m_tmpbuf;
		private Msg m_inProgress;
		private IMsgSink m_msgSink;
		private readonly long m_maxmsgsize;
		private MsgFlags m_msgFlags;
    
		public V1Decoder (int bufsize, long maxmsgsize, IMsgSink session) : base(bufsize)
		{
			m_maxmsgsize = maxmsgsize;
			m_msgSink = session;
        
			m_tmpbuf = new byte[8];
        
			//  At the beginning, read one byte and go to one_byte_size_ready state.
			NextStep(m_tmpbuf, 1, FlagsReadyState);
		}

		//  Set the receiver of decoded messages.
		public override void SetMsgSink (IMsgSink msgSink) 
		{
			m_msgSink = msgSink;
		}

    
		protected override bool Next() {
			switch(State) {
				case OneByteSizeReadyState:
					return OneByteSizeReady ();
				case EightByteSizeReadyState:
					return EightByteSizeReady ();
				case FlagsReadyState:
					return FlagsReady ();
				case MessageReadyState:
					return MessageReady ();
				default:
					return false;
			}
		}



		private bool OneByteSizeReady() {
        
			//  Message size must not exceed the maximum allowed size.
			if (m_maxmsgsize >= 0)
				if (m_tmpbuf [0] > m_maxmsgsize) {
					DecodingError ();
					return false;
				}

			//  in_progress is initialised at this point so in theory we should
			//  close it before calling zmq_msg_init_size, however, it's a 0-byte
			//  message and thus we can treat it as uninitialised...
			m_inProgress = new Msg(m_tmpbuf [0]);

			m_inProgress.SetFlags (m_msgFlags);
			NextStep (m_inProgress.Data , m_inProgress.Size ,MessageReadyState);

			return true;
		}
    
		private bool EightByteSizeReady() {
        
			//  The payload size is encoded as 64-bit unsigned integer.
			//  The most significant byte comes first.        

			long msg_size = m_tmpbuf.GetLong(0);

			//  Message size must not exceed the maximum allowed size.
			if (m_maxmsgsize >= 0)
				if (msg_size > m_maxmsgsize) {
					DecodingError ();
					return false;
				}

			//  Message size must fit within range of size_t data type.
			if (msg_size > int.MaxValue) {
				DecodingError ();
				return false;
			}
        
			//  in_progress is initialised at this point so in theory we should
			//  close it before calling init_size, however, it's a 0-byte
			//  message and thus we can treat it as uninitialised.
			m_inProgress = new Msg ((int) msg_size);

			m_inProgress.SetFlags (m_msgFlags);
			NextStep (m_inProgress.Data , m_inProgress.Size, MessageReadyState);

			return true;
		}
    
		private bool FlagsReady() {

			//  Store the flags from the wire into the message structure.
			m_msgFlags = 0;
			int first = m_tmpbuf[0];
			if ((first & V1Protocol.MoreFlag) > 0)
				m_msgFlags |= MsgFlags.More;
        
			//  The payload length is either one or eight bytes,
			//  depending on whether the 'large' bit is set.
			if ((first & V1Protocol.LargeFlag) > 0)
				NextStep (m_tmpbuf, 8, EightByteSizeReadyState);
			else
				NextStep (m_tmpbuf,1, OneByteSizeReadyState);
        
			return true;

		}
    
		private bool MessageReady() {
			//  Message is completely read. Push it further and start reading
			//  new message. (in_progress is a 0-byte message after this point.)
        
			if (m_msgSink == null)
				return false;
        
			bool rc = m_msgSink.PushMsg (m_inProgress);
			if (!rc) {
				if (ZError.IsError (ErrorNumber.EAGAIN))
					DecodingError ();
            
				return false;
			}

			NextStep(m_tmpbuf, 1, FlagsReadyState);
        
			return true;
		}


		//  Returns true if there is a decoded message
		//  waiting to be delivered to the session.
		public override bool Stalled ()
		{
			return State == MessageReadyState;
		}

	}
}