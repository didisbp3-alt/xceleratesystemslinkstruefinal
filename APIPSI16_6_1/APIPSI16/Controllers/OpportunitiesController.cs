    using APIPSI16.Data;
using APIPSI16.Models;
using APIPSI16.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace APIPSI16.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require JWT for all actions
    public class OpportunitiesController : ControllerBase
    {
        private readonly xcleratesystemslinks_SampleDBContext _context;

        public OpportunitiesController(xcleratesystemslinks_SampleDBContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        // GET: api/Opportunities/recommended  – personalised for current user
        [HttpGet("recommended")]
        public async Task<IActionResult> GetRecommended()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized();

            var user = await _context.Users.FindAsync(uid.Value);
            if (user == null) return NotFound();

            var q = _context.Opportunities.Include(o => o.Company).AsQueryable();

            // Match on EmploymentType or SeniorityLevel based on user's job preference
            if (user.JobPreference.HasValue)
                q = q.Where(o => o.EmploymentType == (byte?)user.JobPreference.Value);

            var results = await q.Select(o => new
            {
                o.Id, o.Title, o.Location,
                LocationId = o.LocationId,
                LocationName = o.LocationNav != null ? o.LocationNav.Name : o.Location,
                CountryName = o.CountryNav != null ? o.CountryNav.Name : null,
                o.EmploymentType, o.SeniorityLevel, o.RemoteOption,
                o.CompanyId, CompanyName = o.Company != null ? o.Company.Name : null
            }).ToListAsync();

            return Ok(results);
        }

        // GET: api/Opportunities
        // All authenticated users can view opportunities
        [HttpGet]
        public async Task<IActionResult> GetOpportunities()
        {
            var opportunities = await _context.Opportunities
                .Select(o => new
                {
                    o.Id,
                    o.Title,
                    o.Location,
                    LocationId = o.LocationId,
                    LocationName = o.LocationNav != null ? o.LocationNav.Name : o.Location,
                    CountryName = o.CountryNav != null ? o.CountryNav.Name : null,
                    o.EmploymentType,
                    o.SeniorityLevel,
                    o.RemoteOption,
                    o.CompanyId,
                    CompanyName = o.Company.Name
                })
                .ToListAsync();

            return Ok(opportunities);
        }

        // GET: api/Opportunities/5
        // All authenticated users can view opportunity details
        [HttpGet("{id}")]
        public async Task<IActionResult> GetOpportunity(int id)
        {
            var opportunity = await _context.Opportunities
                .Include(o => o.Company)
                .Include(o => o.LocationNav)
                    .ThenInclude(l => l != null ? l.Country : null)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (opportunity == null) return NotFound();

            return Ok(opportunity);
        }

        // POST: api/Opportunities
        // Only admins and employers can create opportunities
        [HttpPost]
        [Authorize(Roles = "0,2")] // Admin or Employer
        public async Task<IActionResult> CreateOpportunity([FromBody] Opportunity opportunity)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            // Employers can only create opportunities for companies they're members of (Recruiter, HRManager, or CompanyAdmin)
            if (userRole == "2" && opportunity.CompanyId.HasValue)
            {
                if (!currentUserId.HasValue) return Unauthorized();

                var member = await _context.CompanyMembers
                    .FirstOrDefaultAsync(cm => cm.CompanyId == opportunity.CompanyId.Value && cm.UserId == currentUserId.Value);

                if (member == null)
                    return Forbid("Só podes criar vagas para empresas onde és membro.");
                // Role 1=Recruiter (can create), 2=HRManager (can create), 3=CompanyAdmin (can create)
                if (member.Role < 1)
                    return Forbid("Membro pendente não pode criar vagas. Aguarda a aprovação do admin da empresa.");
            }

            _context.Add(opportunity);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetOpportunity), new { id = opportunity.Id }, opportunity);
        }

        // PUT: api/Opportunities/5
        // Admins can update any; employers can update their company's opportunities
        [HttpPut("{id}")]
        [Authorize(Roles = "0,2")] // Admin or Employer
        public async Task<IActionResult> UpdateOpportunity(int id, [FromBody] Opportunity opportunity)
        {
            if (id != opportunity.Id) return BadRequest();

            var existingOpp = await _context.Opportunities.FindAsync(id);
            if (existingOpp == null) return NotFound();

            var currentUserId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            // Employers can only update opportunities for companies they're active members of (role >= 1)
            if (userRole == "2" && existingOpp.CompanyId.HasValue)
            {
                if (!currentUserId.HasValue) return Unauthorized();

                var member = await _context.CompanyMembers
                    .FirstOrDefaultAsync(cm => cm.CompanyId == existingOpp.CompanyId.Value && cm.UserId == currentUserId.Value);

                if (member == null || member.Role < 1)
                    return Forbid("Não tens permissão para editar vagas desta empresa.");
            }

            _context.Entry(existingOpp).State = EntityState.Detached;
            _context.Entry(opportunity).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OpportunityExists(id)) return NotFound();
                throw;
            }

            return NoContent();
        }

        // DELETE: api/Opportunities/5
        // Only admins can delete opportunities
        [HttpDelete("{id}")]
        [Authorize(Roles = "0")] // Admin only
        public async Task<IActionResult> DeleteOpportunity(int id)
        {
            var opportunity = await _context.Opportunities.FindAsync(id);
            if (opportunity == null) return NotFound();

            _context.Opportunities.Remove(opportunity);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET: api/Opportunities/{id}/match
        // Returns match percentage for the current user vs this opportunity
        [HttpGet("{id}/match")]
        public async Task<IActionResult> GetMatchScore(int id)
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized();

            var opp = await _context.Opportunities.FindAsync(id);
            if (opp == null) return NotFound();

            var score = await CalculateMatchScoreAsync(uid.Value, opp);
            return Ok(new { opportunityId = id, matchScore = score });
        }

        // GET: api/Opportunities/with-match
        // Returns all opportunities with match percentage for the current user
        [HttpGet("with-match")]
        public async Task<IActionResult> GetOpportunitiesWithMatch()
        {
            var uid = GetCurrentUserId();
            if (uid == null) return Unauthorized();

            var user = await _context.Users
                .Include(u => u.LocationNav)
                    .ThenInclude(l => l != null ? l.Country : null)
                .FirstOrDefaultAsync(u => u.UserId == uid.Value);

            var opportunities = await _context.Opportunities
                .Include(o => o.Company)
                .Include(o => o.LocationNav)
                    .ThenInclude(l => l != null ? l.Country : null)
                .ToListAsync();

            var userPrefIds = await _context.UserJobPreferences
                .Where(p => p.UserId == uid.Value)
                .Select(p => p.JobRoleId)
                .ToListAsync();

            var userLocationId = user?.LocationId;
            var userRegion = user?.LocationNav?.Region;
            var userCountryCode = user?.LocationNav?.Country?.Code ?? user?.CountryNav?.Code;

            var results = opportunities.Select(o =>
            {
                var requiredRoleIds = string.IsNullOrWhiteSpace(o.RequiredJobRoleIds)
                    ? new HashSet<int>()
                    : o.RequiredJobRoleIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                        .Where(v => v > 0)
                        .ToHashSet();

                // Job role score (70% weight)
                int roleScore = 0;
                if (requiredRoleIds.Count > 0)
                {
                    var matches = userPrefIds.Count(id => requiredRoleIds.Contains(id));
                    roleScore = (int)Math.Round((double)matches / requiredRoleIds.Count * 100);
                }

                // Location score (30% weight) – use structured IDs when available
                int locationScore;
                if (userLocationId.HasValue && o.LocationId.HasValue)
                {
                    var oppRegion = o.LocationNav?.Region;
                    var oppCountryCode = o.LocationNav?.Country?.Code;
                    locationScore = MatchScoreHelper.ComputeLocationScore(
                        userLocationId, o.LocationId,
                        userRegion, oppRegion,
                        userCountryCode, oppCountryCode);
                }
                else
                {
                    // Fall back to legacy string matching
                    bool legacyMatch = MatchScoreHelper.LocationsMatch(user?.Location, o.Location);
                    bool hasLegacyLocations = !string.IsNullOrWhiteSpace(user?.Location) && !string.IsNullOrWhiteSpace(o.Location);
                    locationScore = hasLegacyLocations ? (legacyMatch ? 100 : 0) : -1;
                }

                int matchScore = MatchScoreHelper.ComputeWeightedScore(roleScore, locationScore, requiredRoleIds.Count > 0);

                var locationName = o.LocationNav?.Name ?? o.Location;
                var countryName = o.LocationNav?.Country?.Name;

                return new
                {
                    o.Id,
                    o.Title,
                    o.Location,
                    LocationId = o.LocationId,
                    LocationName = locationName,
                    CountryName = countryName,
                    o.EmploymentType,
                    o.SeniorityLevel,
                    o.RemoteOption,
                    o.CompanyId,
                    CompanyName = o.Company?.Name,
                    o.RequiredJobRoleIds,
                    MatchScore = matchScore
                };
            }).OrderByDescending(o => o.MatchScore).ToList();

            return Ok(results);
        }

        private async Task<int> CalculateMatchScoreAsync(int userId, Opportunity opp)
        {
            var user = await _context.Users
                .Include(u => u.LocationNav)
                    .ThenInclude(l => l != null ? l.Country : null)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            // Load opportunity's location if not already loaded
            if (opp.LocationNav == null && opp.LocationId.HasValue)
            {
                opp.LocationNav = await _context.Locations
                    .Include(l => l.Country)
                    .FirstOrDefaultAsync(l => l.LocationId == opp.LocationId.Value);
            }

            var requiredRoleIds = string.IsNullOrWhiteSpace(opp.RequiredJobRoleIds)
                ? new HashSet<int>()
                : opp.RequiredJobRoleIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                    .Where(v => v > 0)
                    .ToHashSet();

            int roleScore = 0;
            if (requiredRoleIds.Count > 0)
            {
                var userPrefIds = await _context.UserJobPreferences
                    .Where(p => p.UserId == userId)
                    .Select(p => p.JobRoleId)
                    .ToListAsync();
                var matches = userPrefIds.Count(id => requiredRoleIds.Contains(id));
                roleScore = (int)Math.Round((double)matches / requiredRoleIds.Count * 100);
            }

            int locationScore;
            if (user?.LocationId.HasValue == true && opp.LocationId.HasValue)
            {
                locationScore = MatchScoreHelper.ComputeLocationScore(
                    user.LocationId, opp.LocationId,
                    user.LocationNav?.Region, opp.LocationNav?.Region,
                    user.LocationNav?.Country?.Code, opp.LocationNav?.Country?.Code);
            }
            else
            {
                bool legacyMatch = MatchScoreHelper.LocationsMatch(user?.Location, opp.Location);
                bool hasLegacy = !string.IsNullOrWhiteSpace(user?.Location) && !string.IsNullOrWhiteSpace(opp.Location);
                locationScore = hasLegacy ? (legacyMatch ? 100 : 0) : -1;
            }

            return MatchScoreHelper.ComputeWeightedScore(roleScore, locationScore, requiredRoleIds.Count > 0);
        }

        private bool OpportunityExists(int id)
        {
            return _context.Opportunities.Any(o => o.Id == id);
        }

        private int? GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
        }

        private string? GetCurrentUserRole()
        {
            return User.FindFirst(ClaimTypes.Role)?.Value;
        }
    }
}