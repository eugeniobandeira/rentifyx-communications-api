namespace RentifyxCommunications.Infrastructure.Options;

// No default: the deployed frontend has no fixed domain yet (only an EC2 IP
// that changes on redeploy), so a baked-in URL would be actively misleading
// rather than a sane fallback - same posture as ConnectionStrings__kafka,
// which also has no appsettings.json entry and is required at deploy time.
public sealed record FrontendOptions(string BaseUrl);
