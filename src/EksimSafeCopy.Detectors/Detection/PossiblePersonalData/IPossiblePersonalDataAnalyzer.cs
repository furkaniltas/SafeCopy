using EksimSafeCopy.Core.Models;
using EksimSafeCopy.Detectors.Detection.Normalization;
using DetectionModel = EksimSafeCopy.Core.Models.Detection;

namespace EksimSafeCopy.Detectors.Detection.PossiblePersonalData;

public interface IPossiblePersonalDataAnalyzer
{
    IReadOnlyList<DetectionModel> Analyze(Document document, NormalizedText normalizedText, IReadOnlyList<DetectionModel> existingDetections, CancellationToken cancellationToken = default);
}
