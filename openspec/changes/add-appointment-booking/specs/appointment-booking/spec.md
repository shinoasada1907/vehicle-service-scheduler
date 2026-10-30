## ADDED Requirements

### Requirement: Book a service appointment
The system SHALL let a caller request an appointment for a customer's vehicle, a service type and a dealership at a desired start time. The system SHALL compute the end time as the start time plus the service type's duration, and SHALL confirm the appointment only when a compatible service bay and a qualified technician are both free for the whole range. A confirmed appointment SHALL be stored with the customer, vehicle, dealership, service type, service bay, technician, start time and end time, and SHALL be returned with HTTP 201 Created and a `Location` header. Times in the response SHALL be in UTC, together with the dealership's IANA time zone.

#### Scenario: Successful booking
- **WHEN** a valid request asks for a 60-minute oil change at the London dealership on Monday 2030-01-07 starting at 09:00 UTC, and a general bay and a technician qualified for oil changes are free from 09:00 to 10:00
- **THEN** the system responds 201 with status `Confirmed`, the assigned bay and technician, `startAt` 2030-01-07T09:00:00Z, `endAt` 2030-01-07T10:00:00Z and time zone `Europe/London`

#### Scenario: Start time given with a non-UTC offset
- **WHEN** a request gives `startAt` as 2030-01-07T10:00:00+01:00
- **THEN** the appointment starts at 2030-01-07T09:00:00Z

### Requirement: Resources must be free for the whole duration
The system SHALL assign only a bay whose bay type equals the service type's required bay type and only a technician qualified for the service type. A bay or technician SHALL be considered free only if none of its confirmed appointments overlaps the requested half-open range `[start, end)`. When several bays or technicians are free, the system SHALL choose the first bay and the first technician ordered by name.

#### Scenario: Technician free at the start but busy later
- **WHEN** technician T2 has a confirmed appointment from 09:30 to 10:30 and a booking is requested from 09:00 to 10:00
- **THEN** T2 is not assigned to the booking

#### Scenario: Back-to-back appointments
- **WHEN** a bay and a technician have a confirmed appointment ending at 10:00 and a booking is requested from 10:00 to 11:00
- **THEN** the same bay and technician can be assigned

#### Scenario: Incompatible bay and unqualified technician
- **WHEN** an EV bay and a technician qualified only for wheel alignment are free, and an oil change is requested
- **THEN** neither of them is assigned

#### Scenario: Deterministic choice
- **WHEN** bays "Bay A" and "Bay B" and technicians "Alice" and "Ben" are all free
- **THEN** the booking is assigned "Bay A" and "Alice"

### Requirement: Reject a booking when there is no capacity
When the dealership has compatible bays and qualified technicians but none of one kind, or of both kinds, is free for the whole range, the system SHALL respond 409 Conflict with problem type `no-capacity` and a `shortage` value of `bays`, `technicians` or `bays-and-technicians`.

#### Scenario: Every compatible bay is busy
- **WHEN** both general bays at the London dealership are booked from 09:00 to 10:00 and a third oil change is requested at 09:00
- **THEN** the system responds 409 with type `no-capacity` and shortage `bays`

#### Scenario: Every qualified technician is busy
- **WHEN** a compatible bay is free but every technician qualified for the service is booked during the range
- **THEN** the system responds 409 with type `no-capacity` and shortage `technicians`

### Requirement: Reject a service the dealership cannot offer
When the dealership has no bay of the required type, or no technician qualified for the service type, the system SHALL respond 422 Unprocessable Entity with problem type `service-not-offered`.

#### Scenario: No alignment bay
- **WHEN** a wheel alignment is requested at the Saigon dealership, which has no alignment bay
- **THEN** the system responds 422 with type `service-not-offered`

### Requirement: Enforce booking rules
The system SHALL respond 422 Unprocessable Entity when a request breaks a booking rule: the vehicle is not registered to the customer (`vehicle-not-owned`); the start time is not after the current time, or is not on a 15-minute boundary in the dealership's local time (`invalid-start-time`); or the range does not fit within the dealership's opening hours of one local day, evaluated in the dealership's time zone (`outside-opening-hours`).

#### Scenario: Vehicle registered to another customer
- **WHEN** a customer books with a vehicle registered to a different customer
- **THEN** the system responds 422 with type `vehicle-not-owned`

#### Scenario: Start time in the past
- **WHEN** the start time is earlier than or equal to the current time
- **THEN** the system responds 422 with type `invalid-start-time`

#### Scenario: Start time off the 15-minute grid
- **WHEN** the start time is 09:10 local time
- **THEN** the system responds 422 with type `invalid-start-time`

#### Scenario: Outside opening hours on a closed day
- **WHEN** a booking is requested on a Sunday at the London dealership, which is closed on Sundays
- **THEN** the system responds 422 with type `outside-opening-hours`

#### Scenario: Opening hours follow daylight saving time
- **WHEN** the London dealership opens at 08:00 local time and a 60-minute booking starts at 2030-07-01T06:45:00Z (07:45 British Summer Time)
- **THEN** the system responds 422 with type `outside-opening-hours`

### Requirement: Report unknown references
The system SHALL respond 404 Not Found with problem type `not-found` when the dealership, service type, customer or vehicle in a booking request does not exist.

#### Scenario: Unknown dealership
- **WHEN** a booking request names a dealership id that does not exist
- **THEN** the system responds 404 with type `not-found`

### Requirement: Validate the request format
The system SHALL respond 400 Bad Request with problem type `validation` and per-field errors when `customerId`, `vehicleId`, `dealershipId`, `serviceTypeId` or `startAt` is missing, or when `startAt` is not an ISO 8601 date-time with an explicit UTC offset (`Z` or `±hh:mm`).

#### Scenario: Start time without an offset
- **WHEN** `startAt` is `2030-01-07T09:00:00`
- **THEN** the system responds 400 with type `validation` and an error for `startAt`

#### Scenario: Missing fields
- **WHEN** the request body is an empty JSON object
- **THEN** the system responds 400 with errors for all five fields

### Requirement: Never double book a resource
The system SHALL never store two confirmed appointments whose ranges overlap for the same bay, the same technician or the same vehicle, including when requests are processed concurrently by one or more API instances. This SHALL be enforced by the database. When the database rejects a booking because a concurrent request took the chosen bay or technician, the system SHALL reload the schedule and try again, up to three attempts in total, and then respond 409 with type `no-capacity`.

#### Scenario: Parallel requests for the last free slots
- **WHEN** 12 requests for oil changes with 12 different vehicles at 2030-01-07T09:00:00Z are sent in parallel to the London dealership, which has two general bays
- **THEN** exactly 2 requests receive 201, 10 receive 409, and the database holds no overlapping confirmed appointments for any bay, technician or vehicle

#### Scenario: Losing a race to a concurrent request
- **WHEN** the chosen bay is taken by a concurrent request between reading the schedule and storing the appointment, and another bay and technician are free
- **THEN** the booking is confirmed with the other bay and technician

### Requirement: A vehicle cannot be booked twice at the same time
The system SHALL respond 409 Conflict with problem type `vehicle-already-booked` when the vehicle already has a confirmed appointment that overlaps the requested range, at any dealership.

#### Scenario: Overlapping booking for the same vehicle
- **WHEN** a vehicle has a confirmed appointment from 09:00 to 10:00 and another booking for it is requested from 09:30
- **THEN** the system responds 409 with type `vehicle-already-booked`

### Requirement: Retrieve an appointment
The system SHALL return an appointment by id with HTTP 200 and the same representation as the booking response, and SHALL respond 404 with problem type `not-found` for an unknown id.

#### Scenario: Read back a booked appointment
- **WHEN** a client follows the `Location` header returned by a successful booking
- **THEN** the system responds 200 with the same appointment

#### Scenario: Unknown appointment
- **WHEN** a client requests an appointment id that does not exist
- **THEN** the system responds 404 with type `not-found`

### Requirement: Errors use Problem Details
Every error response SHALL use the RFC 7807 Problem Details format with `type` set to `urn:vehicle-service-scheduler:problem:<slug>`, a `title`, the HTTP `status`, a `detail` message and a `traceId` extension.

#### Scenario: Not found error
- **WHEN** a client requests an unknown appointment
- **THEN** the response body has `type` `urn:vehicle-service-scheduler:problem:not-found`, `status` 404 and a non-empty `traceId`
