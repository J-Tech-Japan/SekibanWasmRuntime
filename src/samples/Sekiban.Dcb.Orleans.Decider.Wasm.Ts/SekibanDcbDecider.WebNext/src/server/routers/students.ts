import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";

const eventApiBaseUrl = process.env.CLIENT_API_BASE_URL ?? process.env.API_BASE_URL;
const projectionModeSchema = z.enum(["memory", "materializedView"]).default("memory");

const studentSchema = z.object({
  studentId: z.string().uuid(),
  name: z.string(),
  maxClassCount: z.number(),
  enrolledClassRoomIds: z.array(z.string().uuid()),
});

const createStudentSchema = z.object({
  name: z.string().min(1, "Student name is required"),
  maxClassCount: z.number().min(1).max(10).default(5),
});

export const studentsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        pageNumber: z.number().default(1),
        pageSize: z.number().default(10),
        waitForSortableUniqueId: z.string().optional(),
        projectionMode: projectionModeSchema,
      })
    )
    .query(async ({ input }) => {
      const params = new URLSearchParams();
      params.set("pageNumber", input.pageNumber.toString());
      params.set("pageSize", input.pageSize.toString());
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const path = input.projectionMode === "materializedView" ? "/api/mv/students" : "/api/students";
      const res = await fetch(`${eventApiBaseUrl}${path}?${params.toString()}`);
      if (!res.ok) {
        throw new Error("Failed to fetch students");
      }
      const data = await res.json();
      return z.array(studentSchema).parse(data);
    }),

  create: publicProcedure
    .input(createStudentSchema)
    .mutation(async ({ input }) => {
      const studentId = crypto.randomUUID();
      const res = await fetch(`${eventApiBaseUrl}/api/students`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          studentId,
          name: input.name,
          maxClassCount: input.maxClassCount,
        }),
      });
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to create student");
      }
      return res.json();
    }),
});
