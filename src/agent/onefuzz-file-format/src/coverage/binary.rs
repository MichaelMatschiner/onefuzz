// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use coverage::binary::BinaryCoverage;

pub mod v0;
pub mod v1;

#[derive(Serialize, Deserialize)]
#[serde(tag = "version", content = "coverage")]
pub enum BinaryCoverageJson {
    #[serde(rename = "0.1")]
    V0(v0::BinaryCoverageJson),

    #[serde(rename = "1.0")]
    V1(v1::BinaryCoverageJson),
}

impl BinaryCoverageJson {
    pub fn deserialize(text: &str) -> Result<Self> {
        // Try unversioned legacy format.
        let v0 = serde_json::from_str::<v0::BinaryCoverageJson>(text);

        if let Ok(v0) = v0 {
            return Ok(Self::V0(v0));
        }

        // Try versioned formats.
        Ok(serde_json::from_str(text)?)
    }
}

impl TryFrom<BinaryCoverageJson> for BinaryCoverage {
    type Error = anyhow::Error;

    fn try_from(json: BinaryCoverageJson) -> Result<Self> {
        use BinaryCoverageJson::*;

        match json {
            V0(v0) => v0.try_into(),
            V1(v1) => v1.try_into(),
        }
    }
}
