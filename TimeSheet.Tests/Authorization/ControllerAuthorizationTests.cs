using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Controllers.Identity;

namespace TimeSheet.Tests.Authorization
{
    // Every controller's role guard, read straight from the attributes — so a new controller
    // (or a copy-pasted one) can't silently ship with the wrong role or no [Authorize].
    public class ControllerAuthorizationTests
    {
        private static readonly string[] RoleAreas = { AppRoles.Admin, AppRoles.Director, AppRoles.Manager, AppRoles.Employee };

        private static readonly Type[] Controllers = typeof(AuthsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToArray();

        public static TheoryData<Type> RoleAreaControllers()
        {
            var data = new TheoryData<Type>();
            foreach (var c in Controllers.Where(c => RoleAreas.Contains(AreaFromNamespace(c))))
                data.Add(c);
            return data;
        }

        public static TheoryData<Type> AllControllers()
        {
            var data = new TheoryData<Type>();
            foreach (var c in Controllers)
                data.Add(c);
            return data;
        }

        [Fact]
        public void Every_Controller_Lives_In_A_Known_Area()
        {
            Controllers.Should().NotBeEmpty();
            Controllers.Select(AreaFromNamespace).Distinct()
                .Should().OnlyContain(area => area == "Identity" || RoleAreas.Contains(area));
        }

        [Theory]
        [MemberData(nameof(AllControllers))]
        public void Controller_Follows_The_Route_Convention(Type controller)
        {
            controller.GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
            controller.GetCustomAttribute<AreaAttribute>()!.RouteValue.Should().Be(AreaFromNamespace(controller));
            controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/[area]/[controller]");
        }

        [Theory]
        [MemberData(nameof(RoleAreaControllers))]
        public void Role_Area_Controller_Requires_Exactly_Its_Own_Role(Type controller)
        {
            var area = AreaFromNamespace(controller);

            var authorize = controller.GetCustomAttributes<AuthorizeAttribute>().ToList();
            authorize.Should().ContainSingle($"{controller.Name} must have one class-level [Authorize]");
            authorize[0].Roles.Should().Be(area);
        }

        [Theory]
        [MemberData(nameof(RoleAreaControllers))]
        public void Role_Area_Controller_Has_No_Anonymous_Or_Different_Role_Actions(Type controller)
        {
            controller.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();

            foreach (var action in Actions(controller))
            {
                action.GetCustomAttribute<AllowAnonymousAttribute>()
                    .Should().BeNull($"{controller.Name}.{action.Name} must not be anonymous");

                foreach (var a in action.GetCustomAttributes<AuthorizeAttribute>())
                    a.Roles.Should().Be(AreaFromNamespace(controller),
                        $"{controller.Name}.{action.Name} must not widen or change the role");
            }
        }

        [Fact]
        public void Login_Is_The_Only_Anonymous_Endpoint()
        {
            var anonymous = Controllers
                .SelectMany(c => Actions(c).Select(a => (Controller: c, Action: a)))
                .Where(x => x.Action.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                            || x.Controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(x => $"{x.Controller.Name}.{x.Action.Name}");

            anonymous.Should().Equal("AuthsController.Login");
        }

        private static IEnumerable<MethodInfo> Actions(Type controller)
            => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName);

        // TimeSheet.PL.Controllers.Director.ProjectsController -> "Director"
        private static string AreaFromNamespace(Type controller)
            => controller.Namespace!.Split('.').Last();
    }
}
