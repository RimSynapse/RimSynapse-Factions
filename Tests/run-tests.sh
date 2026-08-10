#!/usr/bin/env bash
# Runs every Factions behaviour suite without RimWorld, Unity, Harmony or a game install.
#
# The suites exist because the simulation layers that moved here in 0.7 (Sizing/, the production and
# taxation half of Economy/, Military/, Standing/) are deliberately dependency-free: no Find, no
# Harmony, no Unity, no TechLevel. That is what lets them be compiled against the hand-written
# doubles in RimWorldStubs.cs and run anywhere mono exists. Anything that genuinely needs the game
# is not tested here and is not pretended to be.
#
#   Ubuntu/WSL:  sudo apt-get install -y mono-mcs mono-runtime
#   Then:        Tests/run-tests.sh
#
# These layers still compile against Regions-and-Territories source, not its DLL: Integration/ and
# Placement/ stayed there, and the pure rules read WorldObjectKind and ProvinceControl from them.
# RT is therefore a workspace-relative path — the same assumption the build already makes, since
# RimSynapseFactions.csproj references ..\..\Regions-and-Territories\Assemblies.
#
# Exits non-zero if any suite fails to build or fails an assertion. Each binary is removed before
# its build so a compile failure can never be masked by a stale binary reporting a stale pass.
set -u

cd "$(dirname "$0")/.." || exit 1

SRC=Source
RT=../Regions-and-Territories/Source
OUT="${TMPDIR:-/tmp}/factions-tests"
mkdir -p "$OUT"

if [ ! -d "$RT/Integration" ]; then
    echo "Regions-and-Territories source not found at $RT"
    echo "This suite needs the sibling checkout; clone it beside this repo."
    exit 1
fi

MCS_FLAGS="-target:exe -langversion:latest -nowarn:0169,0414,0649,0219"
failures=0

run_suite() {
    name=$1; shift
    binary="$OUT/$name.exe"
    rm -f "$binary"

    if ! mcs $MCS_FLAGS -out:"$binary" "$@"; then
        echo "BUILD FAILED: $name"
        failures=$((failures + 1))
        return
    fi

    if ! mono "$binary"; then
        failures=$((failures + 1))
    fi
}

run_suite sizing \
    Tests/RimWorldStubs.cs Tests/SizingTests.cs \
    $RT/Integration/*.cs $RT/Sizing/*.cs

run_suite production \
    Tests/RimWorldStubs.cs Tests/ProductionTests.cs \
    $RT/Integration/*.cs $RT/Economy/*.cs $RT/Sizing/*.cs $SRC/Economy/*.cs

run_suite taxation \
    Tests/RimWorldStubs.cs Tests/TaxationTests.cs \
    $RT/Integration/*.cs $RT/Economy/*.cs $RT/Sizing/*.cs $SRC/Economy/*.cs

run_suite military \
    Tests/RimWorldStubs.cs Tests/MilitaryTests.cs \
    $RT/Integration/*.cs $RT/Placement/*.cs $SRC/Military/*.cs

run_suite standing \
    Tests/RimWorldStubs.cs Tests/StandingTests.cs \
    $RT/Integration/*.cs $RT/Placement/*.cs $RT/Sizing/*.cs $SRC/Standing/*.cs

run_suite scaling \
    Tests/RimWorldStubs.cs Tests/RimWorldStubsExt.cs Tests/ScalingTests.cs \
    $RT/Integration/*.cs $RT/Placement/*.cs $RT/Economy/*.cs \
    $RT/Sizing/*.cs $SRC/Economy/*.cs $SRC/Military/*.cs $SRC/Standing/*.cs \
    $RT/WorldObjectPlacementUtility.cs $RT/OutpostPlacementUtility.cs \
    $RT/RegionalOwnershipUtility.cs $RT/GeographicProvince.cs \
    $RT/IRegionDemographicProvider.cs $RT/ProvinceAdjacency.cs \
    $RT/SettlementSizeUtility.cs $SRC/ProductionScalingUtility.cs \
    $SRC/TaxationUtility.cs $SRC/MilitaryReachUtility.cs $SRC/FactionStandingUtility.cs

# Not a suite: a type-check over the impure files that cannot be behaviour-tested without a running
# game, but whose signatures can still be held to the shapes they will really meet. This is what
# catches a patch calling a method that no longer exists — the failure mode a mod cannot see until
# the game loads it. Factions_EmpirePatch is in here because it binds by reflection into an optional
# mod; a signature slip there is invisible until Harmony throws at load.
echo
echo "== type-check (impure files, signatures only) =="
rm -f "$OUT/typecheck.dll"
if mcs -target:library -langversion:latest -nowarn:0169,0414,0649,0219,0067 -out:"$OUT/typecheck.dll" \
    Tests/RimWorldStubs.cs Tests/RimWorldStubsExt.cs \
    $RT/Integration/*.cs $RT/Placement/*.cs $RT/Economy/*.cs \
    $RT/Sizing/*.cs $SRC/Economy/*.cs $SRC/Military/*.cs $SRC/Standing/*.cs \
    $RT/WorldObjectPlacementUtility.cs $RT/OutpostPlacementUtility.cs \
    $RT/RegionalOwnershipUtility.cs $RT/GeographicProvince.cs \
    $RT/IRegionDemographicProvider.cs $RT/ProvinceAdjacency.cs \
    $RT/SettlementSizeUtility.cs $SRC/ProductionScalingUtility.cs \
    $SRC/TaxationUtility.cs $SRC/MilitaryReachUtility.cs $SRC/FactionStandingUtility.cs; then
    echo "  type-check clean"
else
    echo "  TYPE-CHECK FAILED"
    failures=$((failures + 1))
fi

echo
if [ "$failures" -eq 0 ]; then
    echo "ALL SUITES PASSED"
    exit 0
fi

echo "$failures SUITE(S) FAILED"
exit 1
