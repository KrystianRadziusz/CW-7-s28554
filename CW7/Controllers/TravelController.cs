using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;

namespace TravelAgencyAPI.Controllers
{
    [ApiController]
    [Route("api")]
    public class TravelController : ControllerBase
    {
        private const string connectionString = "Server=localhost;Database=TravelAgency;Trusted_Connection=True;";

        // GET /api/trips
        // Zwraca wszystkie dostepne wycieczki i liste krajow
        [HttpGet("trips")]
        public IActionResult GetTrips()
        {
            var trips = new List<object>();

            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(@"
                SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                       c.Name AS CountryName
                FROM Trip t
                JOIN Country_Trip ct ON ct.IdTrip = t.IdTrip
                JOIN Country c ON c.IdCountry = ct.IdCountry
            ", connection))
            {
                try
                {
                    connection.Open();
                    var reader = command.ExecuteReader();
                    var tripDict = new Dictionary<int, dynamic>();

                    while (reader.Read())
                    {
                        int idTrip = reader.GetInt32(0);
                        if (!tripDict.ContainsKey(idTrip))
                        {
                            tripDict[idTrip] = new
                            {
                                IdTrip = idTrip,
                                Name = reader.GetString(1),
                                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                                DateFrom = reader.GetDateTime(3),
                                DateTo = reader.GetDateTime(4),
                                MaxPeople = reader.GetInt32(5),
                                Countries = new List<string>()
                            };
                        }
                        ((List<string>)tripDict[idTrip].Countries).Add(reader.GetString(6));
                    }
                    return Ok(tripDict.Values);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
        }

        // GET /api/clients/{id}/trips
        // Zwraca wszystkie wycieczki powiazane z konkretnym klientem
        [HttpGet("clients/{id}/trips")]
        public IActionResult GetClientTrips(int id)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(@"
                SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                       ct.RegisteredAt, ct.PaymentDate
                FROM Client_Trip ct
                JOIN Trip t ON t.IdTrip = ct.IdTrip
                WHERE ct.IdClient = @IdClient
            ", connection))
            {
                command.Parameters.AddWithValue("@IdClient", id);
                try
                {
                    connection.Open();
                    var reader = command.ExecuteReader();
                    var trips = new List<object>();
                    while (reader.Read())
                    {
                        trips.Add(new
                        {
                            IdTrip = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                            DateFrom = reader.GetDateTime(3),
                            DateTo = reader.GetDateTime(4),
                            MaxPeople = reader.GetInt32(5),
                            RegisteredAt = reader.GetInt32(6),
                            PaymentDate = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7)
                        });
                    }
                    if (trips.Count == 0) return NotFound("No trips found for this client or client does not exist.");
                    return Ok(trips);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
        }

        // POST /api/clients
        // Dodaje nowego klienta do bazy
        [HttpPost("clients")]
        public IActionResult AddClient([FromBody] ClientDto client)
        {
            if (string.IsNullOrWhiteSpace(client.FirstName) ||
                string.IsNullOrWhiteSpace(client.LastName) ||
                string.IsNullOrWhiteSpace(client.Email))
                return BadRequest("FirstName, LastName and Email are required.");

            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(@"
                INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                OUTPUT INSERTED.IdClient
                VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)
            ", connection))
            {
                command.Parameters.AddWithValue("@FirstName", client.FirstName);
                command.Parameters.AddWithValue("@LastName", client.LastName);
                command.Parameters.AddWithValue("@Email", client.Email);
                command.Parameters.AddWithValue("@Telephone", (object?)client.Telephone ?? DBNull.Value);
                command.Parameters.AddWithValue("@Pesel", (object?)client.Pesel ?? DBNull.Value);

                try
                {
                    connection.Open();
                    var newId = (int)command.ExecuteScalar();
                    return Created($"/api/clients/{newId}", new { IdClient = newId });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
        }

        // PUT /api/clients/{id}/trips/{tripId}
        // Rejestruje klienta na wycieczke
        [HttpPut("clients/{id}/trips/{tripId}")]
        public IActionResult RegisterClientForTrip(int id, int tripId)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var transaction = connection.BeginTransaction();
                try
                {
                    // sprawdzenie czy klient i wycieczka istnieja
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Client WHERE IdClient = @id", connection, transaction);
                    checkCmd.Parameters.AddWithValue("@id", id);
                    if ((int)checkCmd.ExecuteScalar() == 0)
                        return NotFound("Client not found.");

                    checkCmd.CommandText = "SELECT MaxPeople FROM Trip WHERE IdTrip = @tripId";
                    checkCmd.Parameters.Clear();
                    checkCmd.Parameters.AddWithValue("@tripId", tripId);
                    var maxPeopleObj = checkCmd.ExecuteScalar();
                    if (maxPeopleObj == null)
                        return NotFound("Trip not found.");

                    int maxPeople = (int)maxPeopleObj;

             
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Client_Trip WHERE IdTrip = @tripId";
                    int currentCount = (int)checkCmd.ExecuteScalar();
                    if (currentCount >= maxPeople)
                        return BadRequest("Trip has reached max capacity.");

            
                    var insertCmd = new SqlCommand(@"
                        INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt)
                        VALUES (@id, @tripId, @date)
                    ", connection, transaction);
                    insertCmd.Parameters.AddWithValue("@id", id);
                    insertCmd.Parameters.AddWithValue("@tripId", tripId);
                    insertCmd.Parameters.AddWithValue("@date", int.Parse(DateTime.Now.ToString("yyyyMMdd")));
                    insertCmd.ExecuteNonQuery();

                    transaction.Commit();
                    return Ok("Client registered for trip.");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return StatusCode(500, ex.Message);
                }
            }
        }

        // DELETE /api/clients/{id}/trips/{tripId}
        // Usuwanie rejestracji klienta z danej wycieczki
        [HttpDelete("clients/{id}/trips/{tripId}")]
        public IActionResult DeleteClientTrip(int id, int tripId)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(@"
                DELETE FROM Client_Trip
                WHERE IdClient = @id AND IdTrip = @tripId
            ", connection))
            {
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@tripId", tripId);
                try
                {
                    connection.Open();
                    int rows = command.ExecuteNonQuery();
                    if (rows == 0)
                        return NotFound("Client trip registration not found.");
                    return Ok("Registration deleted.");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, ex.Message);
                }
            }
        }
    }
    
    public class ClientDto
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string? Telephone { get; set; }
        public string? Pesel { get; set; }
    }
}
