using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atelier.Framework.Attributes;
using Atelier.Framework.Primitives;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering;
using Atelier.Framework.Offering.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace {{ namespaceName }};

[Offering]
[Infrastructure(InfrastructureLifetime.Scoped)]
public sealed partial class {{ className }} : global::Atelier.Framework.Offering.GatewayBase, {{ interfaceName }}
{
    [Requisite]
    private readonly {{ interfaceName }} _backend = null!;

    {{ tokenValidatorField }}

    {{ authorizeAsyncMethod }}

    {{ methods }}
}
