namespace EksimSafeCopy.Detectors;

using EksimSafeCopy.Core.Abstractions;
using EksimSafeCopy.Detectors.Detection.Detectors;
using EksimSafeCopy.Detectors.Detection.Pipeline;
using EksimSafeCopy.Detectors.Detection.PossiblePersonalData;
using Microsoft.Extensions.DependencyInjection;

public static class DetectorsModule
{
    public static IServiceCollection AddDetectors(this IServiceCollection services)
    {
        services.AddSingleton<ITurkishIdentityNumberDetector, TurkishIdentityNumberDetector>();
        services.AddSingleton<IPhoneNumberDetector, PhoneNumberDetector>();
        services.AddSingleton<IEmailDetector, EmailDetector>();
        services.AddSingleton<IBirthDateDetector, BirthDateDetector>();
        services.AddSingleton<IPersonNameDetector, PersonNameDetector>();
        services.AddSingleton<IAddressDetector, AddressDetector>();
        services.AddSingleton<IInstallationNumberDetector, InstallationNumberDetector>();
        services.AddSingleton<IPossiblePersonalDataAnalyzer, PossiblePersonalDataAnalyzer>();

        services.AddSingleton<IReadOnlyList<IDetector>>(sp =>
        {
            return new List<IDetector>
            {
                sp.GetRequiredService<ITurkishIdentityNumberDetector>(),
                sp.GetRequiredService<IPhoneNumberDetector>(),
                sp.GetRequiredService<IEmailDetector>(),
                sp.GetRequiredService<IBirthDateDetector>(),
                sp.GetRequiredService<IPersonNameDetector>(),
                sp.GetRequiredService<IAddressDetector>(),
                sp.GetRequiredService<IInstallationNumberDetector>()
            }.AsReadOnly();
        });

        services.AddSingleton<IDetectionEngine>(sp =>
        {
            var detectors = sp.GetRequiredService<IReadOnlyList<IDetector>>();
            var analyzer = sp.GetService<IPossiblePersonalDataAnalyzer>();
            return new DetectionEngine(detectors, analyzer);
        });

        return services;
    }
}

public interface ITurkishIdentityNumberDetector : IDetector { }
public interface IPhoneNumberDetector : IDetector { }
public interface IEmailDetector : IDetector { }
public interface IBirthDateDetector : IDetector { }
public interface IPersonNameDetector : IDetector { }
public interface IAddressDetector : IDetector { }
public interface IInstallationNumberDetector : IDetector { }