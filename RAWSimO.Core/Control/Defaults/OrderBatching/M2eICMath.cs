using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pure arithmetic helpers for the M2e-IC (inbound-committed split) model: P1 gate,
    /// SG split gate, D11 pipeline lead gate, D9 multi-part accounting, D15 pool-local
    /// scarcity and the PK packing budget.
    /// Spec: docs/superpowers/specs/2026-07-16-pod-centric-inbound-split-design.md (v4).
    /// </summary>
    public static class M2eICMath
    {
        /// <summary>
        /// Big-M for the icP1d/icSG2 gates: the most units order o could possibly draw
        /// from a gated pod class this solve = its total remaining demand.
        /// </summary>
        public static int PaDrawBigM(IEnumerable<int> residualUnits)
        {
            if (residualUnits == null)
                throw new ArgumentNullException(nameof(residualUnits));
            checked
            {
                int total = 0;
                foreach (int quantity in residualUnits)
                {
                    if (quantity < 0)
                        throw new ArgumentOutOfRangeException(nameof(residualUnits), "Residual quantities cannot be negative.");
                    total += quantity;
                }
                return total;
            }
        }

        /// <summary>
        /// whole[o] eligibility (icP1c/icP1oos): an existing split parent, or an order
        /// with any out-of-stock residual SKU, can never be whole - it would decode into
        /// split children while P1 still permitted it storage-area draws.
        /// </summary>
        public static bool IsWholeEligible(bool isSplitParent, bool hasInvisibleResidualSku)
        {
            return !isSplitParent && !hasInvisibleResidualSku;
        }

        /// <summary>
        /// (SG-twilight) clock-gated split window: splitting opens during the CURRENT
        /// processing pod's twilight (releaseLeft &lt;= twilightSec), but ONLY once the
        /// station's successor supply is secured (successorSecured = in-flight pods >=
        /// pipeline target). Ordering is load-bearing: without it, twilight squeezing
        /// cannibalizes the whole orders that justify successor trips (eshi13'/P1) and
        /// the seams it was built to close get WORSE (tw-v1: 22 seams vs close 15).
        /// No processing pod or NaN releaseLeft = closed (nothing to squeeze).
        /// </summary>
        public static bool SplitGateOpenTwilight(bool hasProcessingPod, double releaseLeft, double twilightSec, bool successorSecured)
        {
            if (twilightSec <= 0)
                throw new ArgumentOutOfRangeException(nameof(twilightSec));
            if (!hasProcessingPod || double.IsNaN(releaseLeft))
                return false;
            return successorSecured && releaseLeft <= twilightSec;
        }

        /// <summary>
        /// (Scout) Whether an anticipatory dispatch is justified for a (pod, station) pair:
        /// the D11 lead gate must be open, the station's pipeline must still be short of
        /// its target (shortfall &gt; 0, so scout dispatch competes for the SAME per-station
        /// budget as an ordinary D11 successor), and the pod must carry positive
        /// backlog-matching supply (won't be a wasted trip).
        /// </summary>
        public static bool AnticipatoryDispatchOpen(bool pipeGateOpen, int pipelineShortfall, double podCoverage)
        {
            if (pipelineShortfall < 0)
                throw new ArgumentOutOfRangeException(nameof(pipelineShortfall));
            if (podCoverage < 0)
                throw new ArgumentOutOfRangeException(nameof(podCoverage));
            return pipeGateOpen && pipelineShortfall > 0 && podCoverage > 0;
        }

        /// <summary>
        /// Total downstream packing capacity C: abstract packing stations grow the box
        /// pool linearly (stationCount x perStationCapacity, no per-station attribution).
        /// Either factor &lt;= 0 = unlimited (returns the 0 sentinel PackingBudget expects).
        /// </summary>
        public static int TotalPackingCapacity(int stationCount, int perStationCapacity)
        {
            if (stationCount <= 0 || perStationCapacity <= 0)
                return 0;
            return stationCount * perStationCapacity;
        }

        /// <summary>
        /// Remaining packing budget for NEW split parents this solve (icPK2 RHS).
        /// capacity &lt;= 0 = unlimited (int.MaxValue sentinel; PK block skipped anyway).
        /// </summary>
        public static int PackingBudget(int capacity, int aliveParents)
        {
            if (aliveParents < 0)
                throw new ArgumentOutOfRangeException(nameof(aliveParents));
            if (capacity <= 0)
                return int.MaxValue;
            return Math.Max(0, capacity - aliveParents);
        }

        /// <summary>
        /// Decode-side ground truth for the P1 gate: true when a split-path order drew a
        /// positive number of units from storage-area (Pa) pods - a P1 violation.
        /// </summary>
        public static bool SplitOrderDrawsFromStorage(bool tookSplitPath, int paUnitsDrawn)
        {
            if (paUnitsDrawn < 0)
                throw new ArgumentOutOfRangeException(nameof(paUnitsDrawn));
            return tookSplitPath && paUnitsDrawn > 0;
        }

        /// <summary>
        /// (D9) Station-parts beyond the first: max(0, parts - 1). The ep[o] lower bound.
        /// </summary>
        public static int MultiPartExcess(int stationPartCount)
        {
            if (stationPartCount < 0)
                throw new ArgumentOutOfRangeException(nameof(stationPartCount));
            return Math.Max(0, stationPartCount - 1);
        }

        /// <summary>
        /// (D11) Lead gate: open when the station has no processing pod at all, or when
        /// its remaining committed work is within leadSec. NaN releaseLeft with a
        /// processing pod present is treated as "&gt; lead" (defer) - the AE convention.
        /// </summary>
        public static bool PipelineGateOpen(bool hasProcessingPod, double releaseLeftSec, double leadSec)
        {
            if (leadSec < 0)
                throw new ArgumentOutOfRangeException(nameof(leadSec));
            if (!hasProcessingPod)
                return true;
            return !double.IsNaN(releaseLeftSec) && releaseLeftSec <= leadSec;
        }

        /// <summary>
        /// (SG) Split gate: may this station open NEW partial children this solve?
        /// A bridge partial exists to squeeze a LIVE processing pod, so no processing pod
        /// = closed in both modes. Strict additionally requires no queued successor
        /// (the bridge window = the successor's travel window).
        /// </summary>
        public static bool SplitGateOpen(bool hasProcessingPod, int queuedPodCount, bool strictMode)
        {
            if (queuedPodCount < 0)
                throw new ArgumentOutOfRangeException(nameof(queuedPodCount));
            if (!hasProcessingPod)
                return false;
            return !strictMode || queuedPodCount == 0;
        }

        /// <summary>
        /// (D15) Pool-local scarcity of a SKU: min(1, backlog demand / candidate-pool
        /// supply). Zero demand = 0; missing/non-positive supply = maximally scarce (1).
        /// Same formula as PVGS's sigma, but the denominator is THIS solve's candidate
        /// pod pool, not warehouse-wide supply.
        /// </summary>
        public static double PoolScarcity(int backlogDemand, int poolSupply)
        {
            if (backlogDemand < 0)
                throw new ArgumentOutOfRangeException(nameof(backlogDemand));
            if (backlogDemand == 0)
                return 0.0;
            if (poolSupply <= 0)
                return 1.0;
            return Math.Min(1.0, (double)backlogDemand / poolSupply);
        }
    }
}
