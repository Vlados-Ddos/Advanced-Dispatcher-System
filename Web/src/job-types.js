// One presentation catalogue for native and optional job producers.  The
// values on the wire remain the game's stable enum IDs; this table only
// defines how those IDs are presented consistently across the UI.
const definitions = Object.freeze({
  Transport: Object.freeze({ display: "FreightHaul", icon: "Transport", category: "freight", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: ["Transport", "Перевозка"] }),
  FreightHaul: Object.freeze({ display: "FreightHaul", icon: "Transport", category: "freight", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: [] }),
  EmptyHaul: Object.freeze({ display: "LogisticHaul", icon: "EmptyHaul", category: "logistical", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: ["EmptyHaul"] }),
  LogisticHaul: Object.freeze({ display: "LogisticHaul", icon: "EmptyHaul", category: "logistical", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: [] }),
  LogisticalHaul: Object.freeze({ display: "LogisticHaul", icon: "EmptyHaul", category: "logistical", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: [] }),
  ShuntingLoad: Object.freeze({ display: "ShuntingLoad", icon: "ShuntingLoad", category: "shunting", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: ["ShuntingLoad"] }),
  ShuntingUnload: Object.freeze({ display: "ShuntingUnload", icon: "ShuntingUnload", category: "shunting", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: ["ShuntingUnload"] }),
  PassengerLocal: Object.freeze({ display: "PassengerLocal", icon: "PassengerLocal", category: "passenger", colorSource: "passengerBooklet", fallbackColor: "#98a6b0", legacy: ["PassengerLocal"] }),
  PassengerExpress: Object.freeze({ display: "PassengerExpress", icon: "PassengerExpress", category: "passenger", colorSource: "passengerBooklet", fallbackColor: "#98a6b0", legacy: ["PassengerExpress"] }),
  ComplexTransport: Object.freeze({ display: "ComplexTransport", icon: "ComplexTransport", category: "freight", colorSource: "nativeBooklet", fallbackColor: "#98a6b0", legacy: ["ComplexTransport"] }),
});

const unknown = Object.freeze({ display: "unknownJobType", icon: "unknown", category: "unknown", colorSource: "neutral", fallbackColor: "#98a6b0", legacy: [] });

export function jobTypeDefinition(jobOrType) {
  const type = typeof jobOrType === "string" ? jobOrType : jobOrType?.type;
  return definitions[type] || unknown;
}

export function jobTypeDisplayKey(job) {
  return jobTypeDefinition(job).display;
}

export function jobTypeIconKey(job) {
  return jobTypeDefinition(job).icon;
}

export function isLegacyJobName(job, name) {
  return jobTypeDefinition(job).legacy.includes(name);
}

export const JOB_TYPE_DEFINITIONS = definitions;
