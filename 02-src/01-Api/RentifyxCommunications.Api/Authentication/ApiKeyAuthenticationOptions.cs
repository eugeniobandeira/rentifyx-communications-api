using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;

namespace RentifyxCommunications.Api.Authentication;

[SuppressMessage("Sonar", "S2094", Justification = "Marker options type required by the generic authentication handler base class; no extra settings needed.")]
internal sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;
