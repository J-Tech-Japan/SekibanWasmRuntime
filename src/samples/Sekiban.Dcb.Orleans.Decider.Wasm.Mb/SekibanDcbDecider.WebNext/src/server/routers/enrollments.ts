import { z } from "zod";
import { router, publicProcedure } from "../api/trpc";

const eventApiBaseUrl = process.env.CLIENT_API_BASE_URL ?? process.env.API_BASE_URL;

const enrollmentSchema = z.object({
  studentId: z.string().uuid(),
  studentName: z.string(),
  classRoomId: z.string().uuid(),
  className: z.string(),
  enrollmentDate: z.string(),
});

const enrollStudentSchema = z.object({
  studentId: z.string().uuid(),
  classRoomId: z.string().uuid(),
});

const dropStudentSchema = z.object({
  studentId: z.string().uuid(),
  classRoomId: z.string().uuid(),
});

export const enrollmentsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        waitForSortableUniqueId: z.string().optional(),
        projectionMode: z.enum(["memory", "materializedView"]).default("memory"),
      })
    )
    .query(async ({ input }) => {
      const params = new URLSearchParams();
      params.set("projectionMode", input.projectionMode);
      if (input.waitForSortableUniqueId) {
        params.set("waitForSortableUniqueId", input.waitForSortableUniqueId);
      }

      const res = await fetch(`${eventApiBaseUrl}/api/enrollments?${params.toString()}`);
      if (!res.ok) {
        throw new Error("Failed to fetch enrollment data");
      }
      const data = await res.json();
      return z.array(enrollmentSchema).parse(data);
    }),

  enroll: publicProcedure
    .input(enrollStudentSchema)
    .mutation(async ({ input }) => {
      const res = await fetch(`${eventApiBaseUrl}/api/enrollments/add`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          studentId: input.studentId,
          classRoomId: input.classRoomId,
        }),
      });
      if (!res.ok) {
        const error = await res.text();
        throw new Error(error || "Failed to enroll student");
      }
      return res.json();
    }),

  drop: publicProcedure.input(dropStudentSchema).mutation(async ({ input }) => {
    const res = await fetch(
      `${eventApiBaseUrl}/api/enrollments/drop`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          studentId: input.studentId,
          classRoomId: input.classRoomId,
        }),
      }
    );
    if (!res.ok) {
      const error = await res.text();
      throw new Error(error || "Failed to drop student");
    }
    return res.json();
  }),
});
