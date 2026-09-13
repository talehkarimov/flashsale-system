"""Summarize baseline JSON and XE captures; outputs stay beside the input files."""

import json
import pathlib
import statistics
import sys
import xml.etree.ElementTree as ET

NS = {"s": "http://schemas.microsoft.com/sqlserver/2004/07/showplan"}


def summarize(directory):
    directory = pathlib.Path(directory)
    queries = []
    for path in directory.glob("*.xml"):
        root = ET.parse(path).getroot()
        if root.get("truncated") != "0" or root.get("droppedCount") != "0":
            raise RuntimeError(f"Incomplete XE capture: {path}")
        commands, plans = [], []
        for event in root.findall("event"):
            data = {d.get("name"): d.findtext("value") for d in event.findall("data")}
            sql = event.findtext("action[@name='sql_text']/value") or ""
            if event.get("name") == "rpc_completed" and data.get("object_name") == "sp_executesql":
                commands.append({"sql": data.get("statement", sql), "ms": int(data["duration"]) / 1000,
                                 "reads": int(data["logical_reads"]), "writes": int(data["writes"])})
            if event.get("name") != "query_post_execution_showplan":
                continue
            plan = event.find("data[@name='showplan_xml']/value/s:ShowPlanXML", NS)
            if plan is None:
                continue
            plan_path = directory / f"{path.stem}-{len(plans)}.sqlplan"
            ET.ElementTree(plan).write(plan_path, encoding="utf-8", xml_declaration=True)
            operators = []
            for node in plan.findall(".//s:RelOp", NS):
                runtime = node.findall("./s:RunTimeInformation/s:RunTimeCountersPerThread", NS)
                objects = node.findall("./*/s:Object", NS)
                operators.append({
                    "operator": node.get("PhysicalOp"), "logical": node.get("LogicalOp"),
                    "keyLookup": node.find("./s:IndexScan[@Lookup='1']", NS) is not None,
                    "estimatedRows": float(node.get("EstimateRows", "0")),
                    "actualRows": sum(float(r.get("ActualRows", "0")) for r in runtime),
                    "rowsRead": sum(float(r.get("ActualRowsRead", "0")) for r in runtime),
                    "logicalReads": sum(int(r.get("ActualLogicalReads", "0")) for r in runtime),
                    "objects": [o.attrib for o in objects],
                })
            plans.append({"file": plan_path.name, "sql": sql, "durationMs": int(data.get("duration", "0")) / 1000,
                          "operators": operators,
                          "warnings": [ET.tostring(w, encoding="unicode") for w in plan.findall(".//s:Warnings", NS)]})
        entry = {"query": path.stem, "commandCount": len(commands),
                 "reads": sum(c["reads"] for c in commands), "writes": sum(c["writes"] for c in commands),
                 "sqlMs": sum(c["ms"] for c in commands),
                 "medianCommandMs": statistics.median(c["ms"] for c in commands) if commands else None,
                 "commands": commands, "plans": plans}
        queries.append(entry)
        (directory / f"{path.stem}.sql").write_text("\n\n".join(c["sql"] for c in commands), encoding="utf-8")
        print(f'{path.stem}: commands={entry["commandCount"]}, reads={entry["reads"]}, writes={entry["writes"]}, SQL ms={entry["sqlMs"]:.3f}')
    (directory / "query-summary.json").write_text(json.dumps(queries, indent=2), encoding="utf-8")

    for path in sorted(directory.glob("*.json")):
        record = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(record, dict) or "result" not in record:
            continue
        result = record["result"]
        measured = result["success"] if result["workload"] == "outbox" else result["all"]
        print(f'{result["workload"]:12} c={result["concurrency"]:3}: {measured["throughput"]:8.2f}/s '
              f'p50={measured["p50"]:.2f} p95={measured["p95"]:.2f} p99={measured["p99"]:.2f} '
              f'technical={result["technicalErrorRate"]:.4%}')
        if result["workload"] == "outbox":
            terminal = record["backlogBefore"] - record["backlogAfter"]
            print(f'  Verified terminal completions={terminal}; processor successes={result["success"]["count"]}; '
                  f'idle polls={result.get("idle", {}).get("count", "not classified in this run")}')
            if terminal != result["success"]["count"]:
                print("  WARNING: processor attempts are not equivalent to successful completions")


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        summarize(argument)
