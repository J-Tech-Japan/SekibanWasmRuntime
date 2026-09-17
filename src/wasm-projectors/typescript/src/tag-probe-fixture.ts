import { z } from "zod";
import {
  domain,
  event,
  projector,
  stateUnion,
  tagFamily,
  toRuntimeDomain,
  type RuntimeProjectorDefinition,
} from "@sekiban/dcb-domain";

const probeFamily = tagFamily("probe");

const probePing = event("ProbePing", z.object({ note: z.string().optional() }), {
  tags: () => [probeFamily.of("ping")],
});

type ProbeState = {
  readonly observedTagIds: readonly string[];
  readonly version: number;
};

const probeState = stateUnion(
  z.object({
    observedTagIds: z.array(z.string()),
    version: z.number().int().nonnegative(),
  }),
  {
    initial: { observedTagIds: [], version: 0 },
  },
);

const tagProbeProjectorDef = projector({
  id: "TagProbeProjector",
  version: 1,
  tag: probeFamily,
  state: probeState,
  events: [probePing],
  handlers: {
    ProbePing: (state: ProbeState, eventValue) => ({
      observedTagIds: eventValue.tags.map((tag) => tag.id),
      version: state.version + 1,
    }),
  },
});

const tagProbeDomain = domain({
  events: [probePing],
  projectors: [tagProbeProjectorDef],
});

const runtimeDomain = toRuntimeDomain(tagProbeDomain);

export const tagProbeProjector: RuntimeProjectorDefinition =
  runtimeDomain.projectors.find((projector) => projector.id === "TagProbeProjector") ??
  (() => {
    throw new Error("TagProbeProjector was not materialized from the local fixture domain.");
  })();

export const tagProbeSubscribedEventTypes = tagProbeProjector.subscribedEventTypes;
