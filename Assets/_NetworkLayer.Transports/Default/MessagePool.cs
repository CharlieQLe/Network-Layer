using System.Collections.Generic;

namespace NetworkLayer.Transports.Default {
    public class MessagePool {
        private readonly Queue<Message> _freeMessages = new Queue<Message>();

        public Message RetrieveMessage() {
            if (_freeMessages.Count <= 0) return new Message();
            Message message = _freeMessages.Dequeue();
            message.Reset();
            return message;
        }

        public void PoolMessage(Message message) => _freeMessages.Enqueue(message);
    }
}