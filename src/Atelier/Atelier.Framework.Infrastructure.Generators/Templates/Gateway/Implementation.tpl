using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atelier.Framework.Domain.Gateways;
using Atelier.Framework.Abstractions.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace {{ namespaceName }};

[Infrastructure(InfrastructureLifetime.Scoped)]
public partial class {{ className }} : GatewayBase, {{ interfaceName }}
{
    {{ strategyField }}
    {{ methods }}
}
