using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using SuperSafeBank.Core;
using SuperSafeBank.Core.EventBus;
using SuperSafeBank.Core.Models;
using SuperSafeBank.Domain;
using SuperSafeBank.Persistence.EventStore;
using SuperSafeBank.Persistence.Kafka;

namespace SuperSafeBank.Web.API
{
    public static class InfrastructureRegistry
    {
        public static IServiceCollection AddMongoDb(this IServiceCollection services, IConfiguration configuration)
        {
            BsonSerializer.RegisterSerializer(typeof(decimal), new DecimalSerializer(BsonType.Decimal128));

            return services.AddSingleton(ctx =>
                {
                    var connStr = configuration.GetConnectionString("mongo");
                    return new MongoClient(connectionString: connStr);
                })
                .AddSingleton(ctx =>
                {
                    var client = ctx.GetRequiredService<MongoClient>();
                    var database = client.GetDatabase("bankAccounts");
                    return database;
                });
        }

        public static IServiceCollection AddEventStore(this IServiceCollection services, IConfiguration configuration)
        {
            return services.AddSingleton<IEventStoreConnectionWrapper>(ctx =>
                {
                    var connStr = configuration.GetConnectionString("eventstore");
                    return new EventStoreConnectionWrapper(new Uri(connStr));
                }).AddEventsRepository<Customer, Guid>()
                .AddEventProducer<Customer, Guid>(configuration)
                .AddEventsService<Customer, Guid>()
                .AddEventsRepository<Account, Guid>()
                .AddEventProducer<Account, Guid>(configuration)
                .AddEventsService<Account, Guid>();
        }

        private static IServiceCollection AddEventsRepository<TA, TK>(this IServiceCollection services)
            where TA : class, IAggregateRoot<TK>
        {
            return services.AddSingleton<IEventsRepository<TA, TK>>(ctx =>
            {
                var connectionWrapper = ctx.GetRequiredService<IEventStoreConnectionWrapper>();
                var eventDeserializer = ctx.GetRequiredService<IEventDeserializer>();
                return new EventsRepository<TA, TK>(connectionWrapper, eventDeserializer);
            });
        }
        
        private static IServiceCollection AddEventProducer<TA, TK>(this IServiceCollection services, IConfiguration configuration)
            where TA : class, IAggregateRoot<TK>
        {
            return services.AddSingleton<IEventProducer<TA, TK>>(ctx =>
            {
                var connStr = configuration.GetConnectionString("kafka");

                return new EventProducer<TA, TK>("events", connStr);
            });
        }

        private static IServiceCollection AddEventsService<TA, TK>(this IServiceCollection services)
            where TA : class, IAggregateRoot<TK>
        {
            return services.AddSingleton<IEventsService<TA, TK>>(ctx =>
            {
                var eventsProducer = ctx.GetRequiredService<IEventProducer<TA, TK>>();
                var eventsRepo = ctx.GetRequiredService<IEventsRepository<TA, TK>>();

                return new EventsService<TA, TK>(eventsRepo, eventsProducer);
            });
        }
    }
}