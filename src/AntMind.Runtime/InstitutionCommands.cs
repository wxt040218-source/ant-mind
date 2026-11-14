// VERSION=0.1.0
using System.Collections.Immutable;
using AntMind.Contracts;
using AntMind.Core;
namespace AntMind.Runtime;

// 制度热更新与回滚：唯一写 WorldState.Institution 的入口在 Runtime actor 内；不写任何经济台账/分数。
public sealed partial class GameSession {
    private int EffectiveMinimumReserve =>
        world.ActiveParameters?.MinimumReserve ?? world.Config.MinimumReserve;

    private CommandResult ApplyInstitutionCommand(GameCommand c) {
        CommandResult Fail(string code) => new(c.RequestId, false, code, revision);
        if (c.Reason.Length > InstitutionCatalog.MaxReasonLength) return Fail("institution_reason_too_long");
        if (string.IsNullOrWhiteSpace(c.Reason)) return Fail("reason_required");
        var current = world.Institution;
        if (current.Version == int.MaxValue) return Fail("institution_version_not_found");
        var nextVersion = checked(current.Version + 1);
        InstitutionFields fields;
        string reason;
        if (c.Kind == CommandKind.SetInstitution) {
            if (!InstitutionCatalog.IsField(c.InstitutionField)) return Fail("unknown_institution_field");
            if (!InstitutionCatalog.IsTier(c.InstitutionField, c.InstitutionTier)) return Fail("invalid_institution_tier");
            fields = current.Fields.With(c.InstitutionField, c.InstitutionTier);
            reason = c.Reason;
        } else {
            var target = c.TargetInstitutionVersion;
            if (target < 1 || target >= current.Version) return Fail("institution_rollback_conflict");
            var revision = current.History.FirstOrDefault(r => r.Version == target);
            if (revision is null) return Fail("institution_version_not_found");
            fields = revision.Fields;
            reason = "rollback-to-" + target.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        fields.Validate();
        var history = current.History.Length >= InstitutionCatalog.MaxHistory
            ? current.History.RemoveAt(0) : current.History;
        var entry = new InstitutionRevision {
            Version = nextVersion, AppliedTick = world.Tick, Reason = reason, Fields = fields
        };
        var next = new InstitutionState {
            ReservePolicy = fields.ReservePolicy, RationPolicy = fields.RationPolicy,
            LaborPolicy = fields.LaborPolicy, DefensePolicy = fields.DefensePolicy,
            AdmissionPolicy = fields.AdmissionPolicy,
            Version = nextVersion, AppliedTick = world.Tick, Reason = reason,
            History = history.Add(entry)
        };
        next.Validate();
        world = world with { Institution = next };
        world = world with { ActiveParameters = InstitutionMechanics.Resolve(world) };
        revision++;
        return new(c.RequestId, true, "applied", revision);
    }
}
