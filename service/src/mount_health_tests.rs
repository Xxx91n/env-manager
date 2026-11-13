// The three-state mount health mapping (#293).
//
// `expiresAt` is the only health signal the product carries, and nothing writes it, so
// the mapping is the whole story: absent or unparsable maps to `unverified` with a
// machine-readable reason, past maps to `expired`, future maps to `valid`. The old code
// mapped the first two to a green `healthy: true` badge - the defect this ticket closes.
//
// Driven through `get_mount_health_at`, the same mount-path seam the reconcile tick uses:
// a temp file under `%TEMP%`, removed on drop, no real state read or written.

use crate::reconcile::get_mount_health_at;
use crate::rotate_stop_tests::MountFixture;

fn write_mount_fixture(path: &std::path::Path, expires: Option<&str>) {
    let mut mount = serde_json::json!({
        "id": "mount-1",
        "provider": "vault-kv2",
        "name": "TEST_TOKEN",
        "scope": "user",
        "refreshPolicy": "Periodic",
        "createdAt": "2026-01-01T00:00:00Z",
        "schemaVersion": 2
    });
    if let Some(v) = expires {
        mount["expiresAt"] = serde_json::json!(v);
    }
    let raw = serde_json::to_string_pretty(&serde_json::json!([mount]))
        .expect("serialize the mount fixture");
    std::fs::write(path, raw).expect("write the mount fixture");
}

/// Table-driven over the four inputs named by the #292 spec (absent, unparsable, past,
/// future). The response carries the tri-state value plus the machine-readable reason,
/// and the retired boolean never comes back.
#[tokio::test]
async fn health_maps_the_four_expiry_inputs_onto_the_three_states() {
    let fixture = MountFixture::new("health-mapping");
    let cases: [(&str, Option<&str>, &str, serde_json::Value); 4] = [
        ("absent expiry is unverified", None, "unverified", serde_json::json!("noExpiry")),
        ("unparsable expiry is unverified", Some("not-a-timestamp"), "unverified", serde_json::json!("unparsableExpiry")),
        ("past expiry is expired", Some("2020-01-01T00:00:00Z"), "expired", serde_json::json!("expired")),
        ("future expiry is valid", Some("2099-01-01T00:00:00Z"), "valid", serde_json::Value::Null),
    ];
    for (label, expires, state, reason) in cases {
        write_mount_fixture(&fixture.path, expires);
        let health = get_mount_health_at(&fixture.path)
            .await
            .expect("the health summary must answer");
        let mounts = health["mounts"].as_array().expect("mounts array");
        assert_eq!(mounts.len(), 1, "{}", label);
        assert_eq!(mounts[0]["state"], serde_json::json!(state), "{}", label);
        assert_eq!(mounts[0]["reason"], reason, "{}", label);
        assert!(
            mounts[0].get("healthy").is_none(),
            "the boolean must stay gone ({})",
            label
        );
    }
}
