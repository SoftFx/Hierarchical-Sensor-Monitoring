//! Which HSM product each Compose project reports into (#1490).
//!
//! `probe.docker.products` maps a project to the access key of an HSM product of its own; the
//! probe runs one extra collector per such product (same server, computer and module) and routes
//! every sensor of the project's services there. Every other project stays in the probe's main
//! product. The routing is the only difference between the two: the same sensors, alerts and
//! aggregation, under `Docker/<service>` in a dedicated product and `Docker/<project>/<service>`
//! in the main one ([`super::identity::Naming::node`]).

use std::collections::{BTreeMap, HashMap};

use hsm_collector::Collector;

/// The collector (and product) behind every project.
#[derive(Clone)]
pub struct Routes<'c> {
    main: &'c Collector,
    /// Project → (product id, its collector). The product id is the configured `accessKeyFile`,
    /// which is what tells two products apart in the state file.
    dedicated: BTreeMap<String, (String, &'c Collector)>,
}

impl<'c> Routes<'c> {
    /// Every project in the main product (the test harness).
    #[cfg(test)]
    pub fn main_only(main: &'c Collector) -> Self {
        Self {
            main,
            dedicated: BTreeMap::new(),
        }
    }

    /// `dedicated`: `(project, product id, collector)` for each mapped project.
    pub fn new(
        main: &'c Collector,
        dedicated: impl IntoIterator<Item = (String, String, &'c Collector)>,
    ) -> Self {
        Self {
            main,
            dedicated: dedicated
                .into_iter()
                .map(|(project, product, collector)| (project, (product, collector)))
                .collect(),
        }
    }

    /// The collector the project's sensors register with and post to.
    pub fn collector(&self, project: &str) -> &'c Collector {
        self.dedicated
            .get(project)
            .map_or(self.main, |(_, collector)| *collector)
    }

    /// The project's dedicated product id; `None` = the main product.
    pub fn product(&self, project: &str) -> Option<&str> {
        self.dedicated
            .get(project)
            .map(|(product, _)| product.as_str())
    }

    /// Project → product id of every dedicated project, for the naming.
    pub fn dedicated_projects(&self) -> HashMap<String, String> {
        self.dedicated
            .iter()
            .map(|(project, (product, _))| (project.clone(), product.clone()))
            .collect()
    }

    /// For a log line: "the main product" or "its own product (<key file>)".
    pub fn describe(product: Option<&str>) -> String {
        match product {
            None => "the main product".to_string(),
            Some(product) => format!("its own product ({product})"),
        }
    }
}
