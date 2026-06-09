using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Reflection;
using Atelier.Framework.Context;
using Atelier.Framework.Host.Execution;
using Atelier.Framework.Network;
using Atelier.Framework.Offering.Requisition;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Offering;

[TestFixtureRegistry]
public static class OfferingTestFixtures
{
    private const string HAPPY_IDENTITY = "atelier-happy";

    [Fixture(typeof(IContextAccessor))]
    public static IContextAccessor CallerContext()
    {
        var accessor = new LifecycleProbeContextAccessor();
        accessor.SetCurrent(global::Atelier.Framework.Context.Context.Empty.WithAuthorization(
            AuthorizationContext.Create(userId: HAPPY_IDENTITY, tenantId: HAPPY_IDENTITY)));
        return accessor;
    }

    [Fixture(typeof(CreateOfferingRequest))]
    public static CreateOfferingRequest OfferingRequest()
    {
        return new CreateOfferingRequest
        {
            OfferingTypeName = typeof(LifecycleProbeOffering).FullName!,
            ExecutionMode = OfferingExecutionMode.InProcess,
            AutoRegisterDiscovery = false,
        };
    }

    [Fixture(typeof(OfferingManager))]
    public static OfferingManager SeededOfferingManager()
    {
        var manager = (OfferingManager)BuildWithRequisites(typeof(OfferingManager));

        var active = (ConcurrentDictionary<string, global::Atelier.Framework.Host.Execution.HostExecutionContext>)PrivateField(
            typeof(OfferingManager),
            "_activeOfferings").GetValue(manager)!;

        active[HAPPY_IDENTITY] = new global::Atelier.Framework.Host.Execution.HostExecutionContext
        {
            InstanceId = HAPPY_IDENTITY,
            OfferingTypeName = HAPPY_IDENTITY,
            ExecutionMode = OfferingExecutionMode.InProcess,
            State = HostState.Running,
        };

        return manager;
    }

    [Fixture(typeof(OfferingRequisitionService))]
    public static OfferingRequisitionService SeededRequisitionService()
    {
        var service = (OfferingRequisitionService)BuildWithRequisites(typeof(OfferingRequisitionService));

        var trackersField = PrivateField(typeof(OfferingRequisitionService), "_requisitions");
        var dictionary = trackersField.GetValue(service)!;
        var trackerType = dictionary.GetType().GetGenericArguments()[1];

        var tracker = Activator.CreateInstance(trackerType, nonPublic: true)!;
        SetProperty(trackerType, tracker, "RequisitionId", HAPPY_IDENTITY);
        SetProperty(trackerType, tracker, "InstanceId", HAPPY_IDENTITY);
        SetProperty(trackerType, tracker, "RequesterId", HAPPY_IDENTITY);
        SetProperty(trackerType, tracker, "RequesterTenantId", HAPPY_IDENTITY);
        SetProperty(trackerType, tracker, "RequesterType", typeof(object));
        SetProperty(trackerType, tracker, "OfferingType", typeof(object));
        SetProperty(trackerType, tracker, "PlacedZone", typeof(Atelier.Framework.Primitives.Application));
        SetProperty(trackerType, tracker, "RequisitionedAt", DateTime.UtcNow);
        SetProperty(trackerType, tracker, "ReferenceCount", 1);

        var indexer = dictionary.GetType().GetMethod("set_Item")!;
        indexer.Invoke(dictionary, new[] { HAPPY_IDENTITY, tracker });

        return service;
    }

    private static FieldInfo PrivateField(Type type, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    private static void SetProperty(Type type, object target, string name, object value)
    {
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(target, value);
    }

    private static object BuildWithRequisites(Type type)
    {
        var ctor = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var args = ctor.GetParameters()
            .Select(p => AutoMockProvider.For(p.ParameterType))
            .ToArray();

        return ctor.Invoke(args);
    }
}
