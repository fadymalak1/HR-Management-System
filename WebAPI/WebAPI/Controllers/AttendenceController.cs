﻿using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Constants;
using WebAPI.DTOs;
using WebAPI.Extensions;
using WebAPI.Helpers;
using WebAPI.Interfaces;
using WebAPI.Models;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AttendenceController : ControllerBase
    {
        private IAttendence attendenceRepo;
        private IEmployeeRepo employeeRepo;
        private IMapper mapper;
        private IDaysOffRepo daysOffRepo;
        private IWeeklyDaysOff weeklyDaysOffRepo;


        public AttendenceController(IAttendence attendenceRepo, IEmployeeRepo employeeRepo, IMapper mapper, IDaysOffRepo daysOffRepo, IWeeklyDaysOff weeklyDaysOffRepo)
        {
            this.attendenceRepo = attendenceRepo;
            this.employeeRepo = employeeRepo;
            this.mapper = mapper;
            this.daysOffRepo = daysOffRepo;
            this.weeklyDaysOffRepo = weeklyDaysOffRepo;
        }

        [Authorize(Permissions.Attendance.view)]
        [HttpGet]
        public async Task<ActionResult<PagedList<AttendanceDTO>>> GetAll([FromQuery] UserParams userParams)
        {
            var attendances = await attendenceRepo.GetAll(userParams);


            if (attendances.Count == 0)
            {
                return NotFound("Attendence list is empty");
            }

            Response.AddPaginationHeader(new PaginationHeader(attendances.CurrentPage, attendances.TotalPages, attendances.PageSize, attendances.TotalCount));


            return Ok(attendances);
        }

        [Authorize(Permissions.Attendance.create)]
        [HttpPost]
        public ActionResult Add(AttendanceDTO attendanceDTO)
        {
            DaysOff daysOff = daysOffRepo.GetByDay(attendanceDTO.Day); //validation on holidays
            WeeklyDaysOff weeklyDays = weeklyDaysOffRepo.Get();// same 
            if (daysOff != null)
                return BadRequest("This is an Official day off");

            int dayIndex = (int)attendanceDTO.Day.DayOfWeek;

            if (weeklyDays != null && weeklyDays.Days.Contains((DaysName)dayIndex))
            {
                return BadRequest("This is a weekly day off");
            }

            if (attendenceRepo.GetDayByEmpId(attendanceDTO.EmpId, attendanceDTO.Day) != null)// already exist 
                return BadRequest("Attendance for this employee already added!");


            int employeeDepartureHour = (int)employeeRepo.GetById(attendanceDTO.EmpId).Departure.Hour;// time that he must return home 
            int employeeArrivalHour = (int)employeeRepo.GetById(attendanceDTO.EmpId).Arrival.Hour; // time that he must  arrive 


            Attendence attendence;
            if (attendanceDTO.Status == 0) // if he hasn't attend on this day 
            {
                if (attendanceDTO.Arrival?.Hour < employeeArrivalHour) 
                    // if he entered the arrival time before the time he must arrive in , correct it from employee table 
                    attendanceDTO.Arrival = employeeRepo.GetById(attendanceDTO.EmpId).Arrival;

                attendence = mapper.Map<Attendence>(attendanceDTO);

                              // time entered by you in angular in attendance - time he must go home  in it
                              // so it's the calculating the overtime
                var overtime = (int)attendanceDTO.Departure?.Hour - employeeDepartureHour;
                
                // to calculate if he arrived late  
                // any Dto means data entered by admin in angular 
                
                var latetime = (int)attendanceDTO.Arrival?.Hour - employeeArrivalHour;

                //he may do this in database or dto but it's okay to do it here
                attendence.LatetimeInHours = 0;
                attendence.OvertimeInHours = 0;
				// checking the overtime calculated in line 86
				// if he his overtime less than 0 that means that he left early so we are going to deduct from him 
				if (overtime < 0)
                {
                    // by -1 to make it positive don't forget if we entered here that means overtime
                    // is negative so if we calculate late hours it must be positive to deduct money from him
                    // late hours name is a bit tricky but here in this project it means the time
                    // he arrived late or the time he left early 
                    attendence.LatetimeInHours += overtime * -1;
                }

                //else here means he has overtime so we take the value and store it in database
                else
                {
                    attendence.OvertimeInHours += overtime;
                }
             
                // he arrived late 
                if (latetime > 0)
                {
                    attendence.LatetimeInHours += latetime;
                }
                // finnaly we add the attendance record to the database 

                attendenceRepo.Add(attendence);
            }
            else
            {
                attendence = mapper.Map<Attendence>(attendanceDTO);

                attendenceRepo.Add(attendence);
            }
            attendenceRepo.Save();


            return CreatedAtAction("GetDayByEmpId", new { empId = attendence.EmpId, date = attendence.Day }, attendanceDTO);
        }

        [Authorize(Permissions.Attendance.edit)]
        [HttpPut("{empId}")]
        public IActionResult Update([FromRoute] int empId, [FromQuery] DateOnly date, [FromBody] AttendanceDTO attendenceDTO)
        {
            DaysOff daysOff = daysOffRepo.GetByDay(attendenceDTO.Day);
            WeeklyDaysOff weeklyDays = weeklyDaysOffRepo.Get();
            if (daysOff != null)
                return BadRequest("This is an Official day off");

            int dayIndex = (int)attendenceDTO.Day.DayOfWeek;

            if (weeklyDays != null && weeklyDays.Days.Contains((DaysName)dayIndex))
            {
                return BadRequest("This is a weekly day off");
            }


            Attendence existingAttendence = attendenceRepo.GetDayByEmpId(empId, date);
            Employee currentEmployee = employeeRepo.GetById(empId);
            if (existingAttendence == null)
            {
                return BadRequest("Employee with specified Date Not Found");
            }
            existingAttendence.Status = (AttendenceStatus)attendenceDTO.Status;
            if (existingAttendence.Status == AttendenceStatus.Absent)
            {
                existingAttendence.Arrival = null;
                existingAttendence.Departure = null;
                existingAttendence.LatetimeInHours = null;
                existingAttendence.OvertimeInHours = null;
            }
            else
            {
                existingAttendence.Arrival = attendenceDTO.Arrival;
                existingAttendence.Departure = attendenceDTO.Departure;
                var overtime = (int)attendenceDTO.Departure?.Hour - currentEmployee.Departure.Hour;

                var latetime = (int)attendenceDTO.Arrival?.Hour - currentEmployee.Arrival.Hour;

                existingAttendence.LatetimeInHours = 0;
                existingAttendence.OvertimeInHours = 0;
                if (overtime < 0)
                {
                    existingAttendence.LatetimeInHours += overtime * -1;
                }
                else
                {
                    existingAttendence.OvertimeInHours += overtime;
                }
                if (latetime < 0)
                {
                    existingAttendence.OvertimeInHours += latetime * -1;
                }
                else
                {
                    existingAttendence.LatetimeInHours += latetime;
                }
            }
            existingAttendence.Day = attendenceDTO.Day;
            attendenceRepo.Update(empId, date, existingAttendence);
            attendenceRepo.Save();
            return Ok();
        }

        [Authorize(Permissions.Attendance.delete)]
        [HttpDelete("{empId}")]
        public IActionResult Delete([FromRoute] int empId, [FromQuery] DateOnly date)
        {
            Attendence attendence = attendenceRepo.GetDayByEmpId(empId, date);
            if (attendence == null)
            {
                return NotFound();
            }
            attendenceRepo.Delete(empId, date);
            attendenceRepo.Save();
            return NoContent();
        }


        [Authorize(Permissions.Attendance.view)]
        [HttpGet("GetEmployeeDay/{empId}")]
        public IActionResult GetDayByEmpId([FromRoute] int empId, [FromQuery] DateOnly day)
        {
            Attendence? attendence = attendenceRepo.GetDayByEmpId(empId, day);
            if (attendence == null)
            {
                return NotFound();
            }
            AttendanceDTO attendanceDTO = mapper.Map<AttendanceDTO>(attendence);

            return Ok(attendanceDTO);
        }


        [Authorize(Permissions.Attendance.view)]
        [HttpGet("{empId}")]
        public IActionResult GetAttendenceByEmpId(int empId)
        {
            List<AttendanceDTO> attendanceDTOs = new List<AttendanceDTO>();
            List<Attendence> attendences = attendenceRepo.GetAttendenceByEmpId(empId);
            if (attendences == null || attendences.Count == 0)
            {
                return NotFound("No attendance records found for the specified employee.");
            }
            foreach (var attendance in attendences)
            {
                AttendanceDTO attendanceDTO = mapper.Map<AttendanceDTO>(attendance);
                attendanceDTOs.Add(attendanceDTO);
            }
            return Ok(attendanceDTOs);
        }

        [Authorize(Permissions.Attendance.view)]
        [HttpGet("GetByPeriod")]
        public ActionResult GetByPeriod([FromQuery] Period period)
        {

            List<Attendence> attendences = attendenceRepo.GetByPeriod(period.Start, period.End);

            if (attendences.Count() == 0)
                return NotFound("There is no Attendances.");

            List<AttendanceDTO> attendanceDTOs = new List<AttendanceDTO>();

            foreach (var attendance in attendences)
            {
                AttendanceDTO attendanceDTO = mapper.Map<AttendanceDTO>(attendance);
                attendanceDTOs.Add(attendanceDTO);
            }

            return Ok(attendanceDTOs);
        }
    }

    public class Period
    {
        public DateOnly Start { get; set; }
        public DateOnly End { get; set; }
    }
}
