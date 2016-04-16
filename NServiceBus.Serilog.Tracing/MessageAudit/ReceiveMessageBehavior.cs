using System;
using System.Threading.Tasks;
using NServiceBus.Pipeline;
using NServiceBus.Routing;
using NServiceBus.Settings;

namespace NServiceBus.Serilog.Tracing
{
    class ReceiveMessageBehavior : Behavior<IIncomingLogicalMessageContext>
    {
        LogBuilder logBuilder;
        EndpointName endpointName;

        public ReceiveMessageBehavior(ReadOnlySettings settings, LogBuilder logBuilder)
        {
            endpointName = settings.EndpointName();
            this.logBuilder = logBuilder;
        }

        public class Registration : RegisterStep
        {
            public Registration()
                : base("SerilogReceiveMessage", typeof(ReceiveMessageBehavior), "Logs incoming messages")
            {
                InsertBefore(WellKnownStep.MutateIncomingMessages);
            }
        }
        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var logger = logBuilder.GetLogger("NServiceBus.Serilog.MessageReceived");
            var forContext = logger
                .ForContext("ProcessingEndpoint", endpointName.ToString())
                .ForContext("Message", context.Message.Instance, true)
                .ForContext("MessageType", context.Message.MessageType);
            forContext = forContext.AddHeaders(context.Headers);
            forContext.Information("Receive message {MessageType} {MessageId}");
            await next().ConfigureAwait(false);
        }
    }
}
