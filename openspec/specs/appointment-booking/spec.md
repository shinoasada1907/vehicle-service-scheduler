# appointment-booking Specification

## Purpose
Booking a vehicle service appointment that reserves one compatible service bay and one qualified technician for the whole service duration, reading it back, and guaranteeing that no bay, technician or vehicle is ever double booked, including under concurrent requests. Introduced by the change `add-appointment-booking` (archived 2026-11-02).

## Requirements
### Requirement: Book a service appointment
The system SHALL let a caller request an appointment for a customer's vehicle, a service type and a dealership at a desired start time. The end time SHALL be the start time plus the service type's duration. A confirmed appointment SHALL be stored with the customer, vehicle, dealership, service type, service bay, technician, start time and end time, and SHALL be returned with HTTP 201 Created. Times SHALL be stored and returned in UTC, together with the dealership's time zone.

#### Scenario: S1 — Successful booking
- **WHEN** a 60-minute oil change is requested at the London dealership on Monday 2030-01-07 at 09:00 local time, and a general bay and a technician qualified for oil changes are free from 09:00 to 10:00
- **THEN** the system responds 201 with status `Confirmed`, the assigned bay and technician, start 09:00 and end 10:00 in UTC, and the dealership's time zone

#### Scenario: S2 — End time from the service duration
- **WHEN** a 90-minute brake inspection is booked to start at 09:00
- **THEN** the appointment ends at 10:30

#### Scenario: S3 — Start time with a non-UTC offset
- **WHEN** the start time is sent as `2030-01-07T10:00:00+01:00`
- **THEN** the appointment is stored and returned with start `2030-01-07T09:00:00Z`

#### Scenario: S4 — Read back a booked appointment
- **WHEN** a booked appointment is requested by its id
- **THEN** the system responds 200 with the same details that were returned when it was booked

### Requirement: Bay and technician must both be free for the whole duration
The system SHALL assign only a bay whose type matches the bay type the service requires and only a technician qualified for the service. A bay or technician SHALL count as free only if none of its confirmed appointments overlaps the requested half-open range `[start, end)`. When several bays or technicians are free, the system SHALL choose the first bay and the first technician ordered by name.

#### Scenario: S5 — Technician busy later in the range
- **WHEN** a technician is free at 09:00 but has an appointment from 09:30 to 10:30, and a booking from 09:00 to 10:00 is requested
- **THEN** that technician is not assigned

#### Scenario: S6 — Technician busy from before the range
- **WHEN** a technician has an appointment from 08:30 to 09:30, and a booking from 09:00 to 10:00 is requested
- **THEN** that technician is not assigned

#### Scenario: S7 — Bay busy for part of the range
- **WHEN** a bay has an appointment that overlaps part of the requested range
- **THEN** that bay is not assigned

#### Scenario: S8 — Back-to-back appointments
- **WHEN** a bay and a technician have an appointment ending at 10:00, and a booking from 10:00 to 11:00 is requested
- **THEN** the same bay and technician can be assigned

#### Scenario: S9 — Incompatible bay and unqualified technician
- **WHEN** the only free resources for an oil change are a bay of another type (for example an EV bay) or a technician not qualified for oil changes
- **THEN** they are never assigned

#### Scenario: S10 — Deterministic choice
- **WHEN** several compatible bays and several qualified technicians are free
- **THEN** the first bay and the first technician ordered by name are assigned, with the same result every time

### Requirement: Reject a booking when there is no capacity, naming the bottleneck
When the dealership has compatible bays and qualified technicians but not enough of them are free for the whole range, the system SHALL respond 409 Conflict with problem type `no-capacity` and a `shortage` value that says what is missing.

#### Scenario: S11 — Every compatible bay is busy
- **WHEN** every compatible bay is busy during the range while a qualified technician is free
- **THEN** the system responds 409 with type `no-capacity` and shortage `bays`

#### Scenario: S12 — Every qualified technician is busy
- **WHEN** every qualified technician is busy during the range while a compatible bay is free
- **THEN** the system responds 409 with type `no-capacity` and shortage `technicians`

#### Scenario: S13 — Bays and technicians are all busy
- **WHEN** every compatible bay and every qualified technician is busy during the range
- **THEN** the system responds 409 with type `no-capacity` and shortage `bays-and-technicians`

### Requirement: Reject a service the dealership cannot offer
When the dealership has no bay of the required type, or no technician qualified for the service at all, the system SHALL respond 422 Unprocessable Entity with problem type `service-not-offered`.

#### Scenario: S14 — No bay of the required type
- **WHEN** a wheel alignment is requested at the Saigon dealership, which has no alignment bay
- **THEN** the system responds 422 with type `service-not-offered`

#### Scenario: S15 — No qualified technician
- **WHEN** a service is requested at a dealership where no technician is qualified for it
- **THEN** the system responds 422 with type `service-not-offered`

### Requirement: Enforce booking rules
The system SHALL respond 422 Unprocessable Entity when a request breaks a booking rule: the vehicle is not registered to the customer (`vehicle-not-owned`); the start time is not after the current time, or not on a 15-minute boundary in the dealership's local time (`invalid-start-time`); or the range does not fit within the dealership's opening hours on one local day, evaluated in the dealership's time zone (`outside-opening-hours`).

#### Scenario: S16 — Vehicle registered to another customer
- **WHEN** a customer books with a vehicle registered to a different customer
- **THEN** the system responds 422 with type `vehicle-not-owned`

#### Scenario: S17 — Start time in the past or now
- **WHEN** the start time is earlier than or equal to the current time
- **THEN** the system responds 422 with type `invalid-start-time`

#### Scenario: S18 — Start time off the 15-minute grid
- **WHEN** the start time is 09:10 local time
- **THEN** the system responds 422 with type `invalid-start-time`

#### Scenario: S19 — Outside opening hours
- **WHEN** a booking falls on a Sunday when the dealership is closed, starts before opening time, or ends after closing time
- **THEN** the system responds 422 with type `outside-opening-hours`

#### Scenario: S20 — Opening hours in the dealership's time zone, including summer time
- **WHEN** the London dealership opens at 08:00 local time and 60-minute bookings start at `2030-01-07T08:00:00Z` (winter), `2030-07-01T07:00:00Z` (08:00 British Summer Time) and `2030-07-01T06:45:00Z` (07:45 British Summer Time)
- **THEN** the first two are accepted and the third is rejected with 422 type `outside-opening-hours`

#### Scenario: S21 — Ending exactly at closing time
- **WHEN** the dealership closes at 18:00 and a booking from 17:00 to 18:00 is requested
- **THEN** the booking is accepted

### Requirement: Report unknown references and malformed requests
The system SHALL respond 404 Not Found with problem type `not-found` for an unknown dealership, service type, customer, vehicle or appointment, and 400 Bad Request with problem type `validation` and per-field errors for a malformed request.

#### Scenario: S22 — Unknown reference
- **WHEN** a booking request names a dealership, service type, customer or vehicle that does not exist
- **THEN** the system responds 404 with type `not-found`, saying which one does not exist

#### Scenario: S23 — Missing required fields
- **WHEN** required fields are missing from the booking request
- **THEN** the system responds 400 with type `validation` and an error for each missing field

#### Scenario: S24 — Start time without an offset
- **WHEN** the start time is sent without a UTC offset, for example `2030-01-07T09:00:00`
- **THEN** the system responds 400 with type `validation` and an error for `startAt`

#### Scenario: S25 — Unknown appointment
- **WHEN** an appointment id that does not exist is requested
- **THEN** the system responds 404 with type `not-found`

### Requirement: Never double book a resource
The system SHALL never store two confirmed appointments whose ranges overlap for the same bay, the same technician or the same vehicle, including when requests run concurrently on one or more API instances. The database SHALL enforce this. When a concurrent request takes the chosen bay or technician, the system SHALL reload and try another free pair, up to three attempts in total, and then respond 409 with type `no-capacity`.

#### Scenario: S26 — Parallel requests for the last free slots
- **WHEN** 12 bookings for the same start time with 12 different vehicles are sent in parallel to the London dealership, which has two general bays
- **THEN** exactly 2 succeed with 201, 10 receive 409, and the database holds no overlapping confirmed appointments for any bay, technician or vehicle

#### Scenario: S27 — Losing a race while another pair is free
- **WHEN** the chosen bay is taken by a concurrent request just before the appointment is stored, and another bay and technician are free
- **THEN** the booking is confirmed with the other bay and technician

#### Scenario: S28 — Losing the race too many times
- **WHEN** a booking loses to concurrent requests on all three attempts
- **THEN** the system responds 409 with type `no-capacity`

#### Scenario: S29 — The database rejects overlaps on its own
- **WHEN** an appointment overlapping a confirmed appointment for the same bay, the same technician or the same vehicle is written directly to the database, without going through the API
- **THEN** the database rejects it

### Requirement: A vehicle cannot be booked twice at the same time
The system SHALL respond 409 Conflict with problem type `vehicle-already-booked` when the vehicle already has a confirmed appointment that overlaps the requested range, at any dealership.

#### Scenario: S30 — Overlapping booking for the same vehicle
- **WHEN** a vehicle has an appointment from 09:00 to 10:00 and another booking for it is requested at 09:30, at the same or another dealership
- **THEN** the system responds 409 with type `vehicle-already-booked`

### Requirement: Errors use Problem Details
Every error response SHALL use the RFC 7807 Problem Details format with a stable `type` of the form `urn:vehicle-service-scheduler:problem:<slug>`, a `title`, the HTTP `status`, a `detail` message and a `traceId` for finding the request in logs and traces.

#### Scenario: S31 — Error response format
- **WHEN** any request fails
- **THEN** the response body is Problem Details with a stable `type`, `title`, `status`, `detail` and a non-empty `traceId`

